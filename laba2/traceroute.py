import os
import sys
import socket
import struct
import time
import select
import ipaddress


class NetHelper:
    """
    Вспомогательный класс для работы с IP-адресами и DNS.
    Содержит методы для проверки корректности IP-адреса и обратного разрешения DNS.
    """

    @staticmethod
    def is_ip_valid(addr: str) -> bool:
        """
        Проверяет, является ли строка корректным IP-адресом.

        :param addr: строка с IP-адресом
        :return: True, если адрес корректен, иначе False
        """
        try:
            ipaddress.ip_address(addr)
            return True
        except ValueError:
            return False

    @staticmethod
    def reverse_dns(ip: str) -> str:
        """
        Выполняет обратное разрешение DNS: получает доменное имя по IP-адресу.

        :param ip: IP-адрес в виде строки
        :return: доменное имя или сообщение об ошибке
        """
        try:
            return socket.gethostbyaddr(ip)[0]
        except socket.herror:
            return "DNS имя не найдено"


class ICMPHandler:
    """
    Класс для формирования ICMP-пакетов.
    Реализует вычисление контрольной суммы и сборку пакета для echo-запроса.
    """
    ECHO_REQ = 8  # Тип ICMP для echo-запроса

    @staticmethod
    def calc_checksum(data: bytes) -> int:
        """
        Вычисляет 16-битную контрольную сумму для набора байтов.

        :param data: входные данные (байты)
        :return: рассчитанная контрольная сумма
        """
        total = 0
        n = len(data)

        # Обрабатываем данные парами байтов
        for i in range(0, n - 1, 2):
            word = (data[i + 1] << 8) + data[i]
            total += word
            total &= 0xffffffff  # Ограничиваем сумму 32 битами

        # Если число байтов нечётное, добавляем последний байт
        if n % 2:
            total += data[-1]
            total &= 0xffffffff

        total = (total >> 16) + (total & 0xFFFF)
        total += (total >> 16)
        checksum = ~total & 0xFFFF
        # Приводим к сетевому порядку байтов (big-endian)
        return (checksum >> 8) | ((checksum & 0xFF) << 8)

    @staticmethod
    def build_echo_packet(ident: int, seq: int) -> bytes:
        """
        Собирает ICMP echo request пакет с заданными идентификатором и номером последовательности.

        :param ident: идентификатор (обычно PID процесса)
        :param seq: номер последовательности
        :return: готовый пакет в виде байтов
        """
        # Формат "bbHHh":
        #   b  - 1 байт: тип (ECHO_REQ)
        #   b  - 1 байт: код (0)
        #   H  - 2 байта: контрольная сумма (пока 0)
        #   H  - 2 байта: идентификатор пакета
        #   h  - 2 байта: номер последовательности
        header = struct.pack("bbHHh", ICMPHandler.ECHO_REQ, 0, 0, ident, seq)
        payload = b'bsuirbsuirbsuirbsuirbsuirbsuirpi'
        # Вычисляем контрольную сумму для полного пакета (заголовок+данные)
        chksum = ICMPHandler.calc_checksum(header + payload)
        header = struct.pack("bbHHh", ICMPHandler.ECHO_REQ, 0, socket.htons(chksum), ident, seq)
        return header + payload


class RouteTracer:
    """
    Класс для выполнения трассировки маршрута с ICMP запросами.
    Отправляет пакеты с увеличивающимся TTL, чтобы определить цепочку маршрутизаторов к цели.
    """

    def __init__(self, target_ip: str, max_steps: int = 30, pph: int = 3, time_out: int = 2):
        """
        Инициализирует параметры трассировки.

        :param target_ip: Целевой IP-адрес (в виде строки)
        :param max_steps: Максимальное количество хопов
        :param pph: Количество пакетов, отправляемых на каждом хопе (packets per hop)
        :param time_out: Тайм-аут ожидания ответа в секундах
        """
        self.target_ip = target_ip
        self.max_steps = max_steps
        self.pph = pph
        self.time_out = time_out
        self.ident = os.getpid() & 0xFFFF  # Используем PID процесса для идентификации

    def send_request(self, ttl_value: int) -> (str, float):
        """
        Отправляет один ICMP echo запрос с заданным TTL.

        :param ttl_value: Значение TTL для пакета
        :return: Кортеж (IP-адрес отправителя, round-trip time в мс) или (None, None) при ошибке
        """
        try:
            # Создаем raw-сокет для отправки ICMP-пакета
            s = socket.socket(socket.AF_INET, socket.SOCK_RAW, socket.IPPROTO_ICMP)
            s.setsockopt(socket.IPPROTO_IP, socket.IP_TTL, ttl_value)
            s.settimeout(self.time_out)
        except socket.error as err:
            print("Ошибка создания сокета:", err)
            return None, None

        packet = ICMPHandler.build_echo_packet(self.ident, 1)

        try:
            s.sendto(packet, (self.target_ip, 0))
            send_time = time.time()
        except socket.error as err:
            print("Ошибка отправки пакета:", err)
            s.close()
            return None, None

        while True:
            try:
                ready = select.select([s], [], [], self.time_out)
                if not ready[0]:
                    print("Таймаут ожидания ответа")
                    s.close()
                    return None, None

                recv_data, addr = s.recvfrom(1024)
                recv_time = time.time()
                # Предполагаем, что заголовок IPv4 занимает 20 байт, затем следует ICMP-заголовок
                icmp_segment = recv_data[20:28]
                icmp_type, code, chk, rcv_ident, seq = struct.unpack("bbHHh", icmp_segment)
                # Если получен Echo Reply (тип 0) и идентификатор совпадает
                if icmp_type == 0 and rcv_ident == self.ident:
                    s.close()
                    return addr[0], (recv_time - send_time) * 1000
                # Если получено сообщение о превышении времени (Time Exceeded, тип 11)
                elif icmp_type == 11 and code == 0:
                    s.close()
                    return addr[0], (recv_time - send_time) * 1000
            except socket.error as exc:
                print("Ошибка при получении ответа:", exc)
                s.close()
                return None, None

    def start_tracing(self, use_dns: bool = False):
        """
        Запускает процесс трассировки маршрута до целевого узла.
        :param use_dns: Если True, выполняется обратное разрешение DNS для каждого хопа.
        """
        if use_dns:
            print(f"Traceroute до {self.target_ip} с DNS разрешением (макс. {self.max_steps} хопов):")
        else:
            print(f"Traceroute до {self.target_ip} (макс. {self.max_steps} хопов):")

        for ttl in range(1, self.max_steps + 1):
            results = []
            for _ in range(self.pph):
                ip_resp, rtt = self.send_request(ttl)
                if ip_resp:
                    results.append((ip_resp, rtt))
            if results:
                avg_rtt = sum(delay for _, delay in results) / len(results)
                if use_dns:
                    dns_label = NetHelper.reverse_dns(results[0][0])
                    print(f"{ttl}\t{results[0][0]}\t{dns_label}\t{avg_rtt:.2f} ms")
                else:
                    print(f"{ttl}\t{results[0][0]}\t{avg_rtt:.2f} ms")
                if results[0][0] == self.target_ip:
                    print("Целевой узел достигнут.")
                    break
            else:
                print(f"{ttl}\t*\t*")


def main():
    """
    Основная функция, запрашивающая у пользователя целевой IP или доменное имя,
    а затем запускающая трассировку маршрута.
    """
    target_input = input("Введите IP или доменное имя: ").strip()
    if NetHelper.is_ip_valid(target_input):
        tracer = RouteTracer(target_input)
        tracer.start_tracing(use_dns=False)
    else:
        try:
            ip_addr = socket.gethostbyname(target_input)
            print(f"IP адрес {target_input}: {ip_addr}")
            tracer = RouteTracer(ip_addr)
            tracer.start_tracing(use_dns=True)
        except socket.gaierror:
            print("Ошибка разрешения доменного имени.")


if __name__ == "__main__":
    main()