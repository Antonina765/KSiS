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
    Содержит методы для проверки корректности IP-адреса
    и обратного разрешения DNS по IP.
    """

    @staticmethod
    def is_ip_valid(addr: str) -> bool:
        """
        Проверяет, что строка является корректным IP-адресом.

        :param addr: Строка с IP-адресом.
        :return: True, если адрес корректен, иначе False.
        """
        try:
            ipaddress.ip_address(addr)
            return True
        except ValueError:
            return False

    @staticmethod
    def reverse_dns(ip: str) -> str:
        """
        Пытается получить DNS-имя по IP-адресу.

        :param ip: IP-адрес в виде строки.
        :return: Полученное доменное имя или сообщение об отсутствии.
        """
        try:
            return socket.gethostbyaddr(ip)[0]
        except socket.herror:
            return "DNS имя не найдено"


class ICMPHandler:
    """
    Класс для формирования и обработки ICMP-пакетов.
    Реализует вычисление контрольной суммы и сборку пакета для
    ICMP echo запроса.
    """
    ECHO_REQ = 8  # Константа для типа ICMP эхо-запроса

    @staticmethod
    def calc_checksum(data: bytes) -> int:
        """
        Вычисляет 16-битную контрольную сумму для входящих данных.

        :param data: Данные в виде последовательности байтов.
        :return: Вычисленная контрольная сумма.
        """
        total = 0
        n = len(data)
        # Обрабатываем данные парами байтов.
        for i in range(0, n - 1, 2):
            word = (data[i + 1] << 8) + data[i]
            total += word
            total &= 0xffffffff  # Ограничиваем сумму 32 битами
        # Если число байтов нечётное, добавляем последний байт.
        if n % 2:
            total += data[-1]
            total &= 0xffffffff

        total = (total >> 16) + (total & 0xFFFF)
        total += (total >> 16)
        checksum = ~total & 0xFFFF
        # Переставляем байты для сетевого порядка (big-endian)
        return (checksum >> 8) | ((checksum & 0xFF) << 8)

    @staticmethod
    def build_echo_packet(ident: int, seq: int) -> bytes:
        """
        Собирает ICMP echo request пакет с заданными идентификатором и номером последовательности.

        :param ident: Идентификатор (обычно PID процесса)
        :param seq: Номер последовательности пакета.
        :return: Готовый ICMP пакет в виде байтов.
        """
        # Формат "bbHHh":
        #  b - 1 байт: тип (ECHO_REQ)
        #  b - 1 байт: код (0)
        #  H - 2 байта: контрольная сумма (пока 0)
        #  H - 2 байта: идентификатор
        #  h - 2 байта: номер последовательности
        header = struct.pack("bbHHh", ICMPHandler.ECHO_REQ, 0, 0, ident, seq)
        payload = b'bsuirbsuirbsuirbsuirbsuirbsuirpi'  # Произвольный набор данных
        chksum = ICMPHandler.calc_checksum(header + payload)
        header = struct.pack("bbHHh", ICMPHandler.ECHO_REQ, 0, socket.htons(chksum), ident, seq)
        return header + payload


class RouteTracer:
    """
    Класс для выполнения трассировки маршрута с помощью ICMP-пакетов.
    Отправляет запросы с возрастающим значением TTL и выводит для каждого хопа ответы.
    """

    def __init__(self, target_ip: str, max_hops: int = 30, probes_per_hop: int = 3, timeout: int = 2):
        """
        Инициализирует параметры трассировки.

        :param target_ip: Целевой IP-адрес.
        :param max_hops: Максимальное число хопов (шагов).
        :param probes_per_hop: Количество пакетов на каждый хоп.
        :param timeout: Таймаут ожидания ответа в секундах.
        """
        self.target_ip = target_ip
        self.max_hops = max_hops
        self.probes_per_hop = probes_per_hop
        self.timeout = timeout
        self.ident = os.getpid() & 0xFFFF  # Используем PID процесса как идентификатор

    def send_request(self, ttl: int) -> (str, float):
        """
        Отправляет один ICMP echo запрос с заданным TTL.

        :param ttl: Значение TTL для пакета.
        :return: Кортеж (IP-адрес, время round-trip в мс) или (None, None) при ошибке.
        """
        try:
            # Создаем raw сокет для ICMP.
            sock = socket.socket(socket.AF_INET, socket.SOCK_RAW, socket.IPPROTO_ICMP)
            sock.setsockopt(socket.IPPROTO_IP, socket.IP_TTL, ttl)
            sock.settimeout(self.timeout)
        except socket.error as err:
            print("Ошибка создания сокета:", err)
            return None, None

        packet = ICMPHandler.build_echo_packet(self.ident, 1)
        try:
            sock.sendto(packet, (self.target_ip, 0))
            timestamp_send = time.time()
        except socket.error as err:
            print("Ошибка отправки пакета:", err)
            sock.close()
            return None, None

        while True:
            try:
                ready = select.select([sock], [], [], self.timeout)
                if not ready[0]:
                    # Таймаут: ответа не получено.
                    sock.close()
                    return None, None

                recv_data, addr = sock.recvfrom(1024)
                timestamp_recv = time.time()
                # Пропускаем IPv4-заголовок (20 байт), далее следует ICMP заголовок.
                icmp_data = recv_data[20:28]
                icmp_type, code, chk, rcv_ident, seq = struct.unpack("bbHHh", icmp_data)
                # Если получен Echo Reply (тип 0) и идентификатор совпадает – успех.
                if icmp_type == 0 and rcv_ident == self.ident:
                    sock.close()
                    return addr[0], (timestamp_recv - timestamp_send) * 1000
                # Если получено сообщение "Time Exceeded" (тип 11), значит TTL истёк.
                elif icmp_type == 11 and code == 0:
                    sock.close()
                    return addr[0], (timestamp_recv - timestamp_send) * 1000
            except socket.error as exc:
                print("Ошибка при получении ответа:", exc)
                sock.close()
                return None, None

    def start_tracing(self, use_dns: bool = False):
        """
        Запускает процесс трассировки маршрута и выводит результаты для каждого хопа.
        Для каждого TTL выводятся три отдельных времени (один для каждого пакета).

        :param use_dns: Если True, для каждого хопа дополнительно выводится DNS-имя.
        """
        if use_dns:
            print(f"Traceroute до {self.target_ip} с DNS (макс. {self.max_hops} хопов):")
        else:
            print(f"Traceroute до {self.target_ip} (макс. {self.max_hops} хопов):")

        for ttl in range(1, self.max_hops + 1):
            # Для каждого TTL отправляем заданное число запросов.
            responses = []
            for _ in range(self.probes_per_hop):
                resp_ip, resp_time = self.send_request(ttl)
                # Если ответа нет, сохраняем "*" и None.
                if resp_ip is None:
                    responses.append(("*", None))
                else:
                    responses.append((resp_ip, resp_time))
            # Определяем отображаемый IP (если хотя бы в одном запросе получен ответ).
            valid_ips = [ip for ip, _ in responses if ip != "*"]
            display_ip = valid_ips[0] if valid_ips else "*"
            if use_dns and display_ip != "*":
                dns_label = NetHelper.reverse_dns(display_ip)
                ip_display = f"{dns_label} ({display_ip})"
            else:
                ip_display = display_ip
            # Формируем строку с временем для каждого пакета: если время получено, выводим его,
            # иначе символ "*" для тайм-аута.
            times_str = "\t".join([f"{rt:.2f} ms" if rt is not None else "*" for _, rt in responses])
            # Выводим номер хопа, IP (и имя при необходимости) и три времена.
            print(f"{ttl}\t{ip_display}\t{times_str}")

            # Если хотя бы один из ответов совпадает с целевым IP – завершаем трассировку.
            if any(ip == self.target_ip for ip, _ in responses):
                print("Целевой узел достигнут.")
                break


def main():
    """
    Основная функция. Запрашивает у пользователя целевой IP или доменное имя,
    разрешает его при необходимости, и запускает процедуру трассировки.
    """
    user_input = input("Введите IP или доменное имя: ").strip()
    if NetHelper.is_ip_valid(user_input):
        tracer = RouteTracer(user_input)
        tracer.start_tracing(use_dns=False)
    else:
        try:
            resolved_ip = socket.gethostbyname(user_input)
            print(f"IP адрес {user_input}: {resolved_ip}")
            tracer = RouteTracer(resolved_ip)
            tracer.start_tracing(use_dns=True)
        except socket.gaierror:
            print("Ошибка разрешения доменного имени.")


if __name__ == "__main__":
    """
    Для корректной работы raw-сокетов на macOS обязательно запускайте этот скрипт с повышенными привилегиями,
    например, с помощью команды:

      sudo python3 traceroute.py
    """
    main()
