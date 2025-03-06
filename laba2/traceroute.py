import socket
import os
import struct
import time
import select
import sys

if len(sys.argv) < 2:
    print("Использование: sudo python traceroute.py <IP-адрес или имя узла> [--resolve]")
    sys.exit(1)

ICMP_ECHO_REQUEST = 8
ICMP_PROTO = socket.getprotobyname('icmp')


def calculate_checksum(data):
    """
    Вычисляет контрольную сумму для ICMP-пакета.
    """
    checksum_val = 0
    count = 0
    count_to = (len(data) // 2) * 2
    while count < count_to:
        # Собираем 16-битное число из двух последовательных байтов.
        this_val = data[count + 1] * 256 + data[count]
        checksum_val += this_val
        checksum_val &= 0xffffffff  # Ограничение до 32 бит
        count += 2
    if count_to < len(data):
        checksum_val += data[-1]
        checksum_val &= 0xffffffff

    checksum_val = (checksum_val >> 16) + (checksum_val & 0xffff)
    checksum_val += (checksum_val >> 16)
    answer = ~checksum_val & 0xffff
    # Перестановка байтов для сетевого порядка (big-endian)
    answer = (answer >> 8) | ((answer & 0xff) << 8)
    return answer


def build_packet(seq_number):
    """
    Создает ICMP эхо-запрос (ping) пакет с заданным sequence number.
    """
    pid = os.getpid() & 0xFFFF
    header = struct.pack("bbHHh", ICMP_ECHO_REQUEST, 0, 0, pid, seq_number)
    data = struct.pack("d", time.time())
    packet = header + data
    chksum = calculate_checksum(packet)
    header = struct.pack("bbHHh", ICMP_ECHO_REQUEST, 0, socket.htons(chksum), pid, seq_number)
    return header + data


def send_icmp(sock, dest_ip, seq_number, ttl):
    """
    Отправляет ICMP-пакет с заданным TTL.
    """
    sock.setsockopt(socket.SOL_IP, socket.IP_TTL, ttl)
    packet = build_packet(seq_number)
    sock.sendto(packet, (dest_ip, 1))


def receive_icmp(sock, timeout):
    """
    Ожидает ICMP-ответ в течение timeout секунд.
    Возвращает кортеж (задержка, адрес отправителя) или (None, None), если таймаут.
    """
    time_left = timeout
    while time_left > 0:
        start_select = time.time()
        ready = select.select([sock], [], [], time_left)
        time_spent = time.time() - start_select
        if not ready[0]:
            return None, None

        time_received = time.time()
        rec_packet, addr = sock.recvfrom(1024)
        # Предположим, что IPv4-заголовок занимает 20 байт (без опций)
        icmp_header = rec_packet[20:28]
        icmp_type, code, recv_checksum, p_id, seq = struct.unpack("bbHHh", icmp_header)

        # Принимаем echo reply (тип 0) и сообщение "TTL exceeded" (тип 11)
        if icmp_type in (0, 11):
            return time_received - start_select, addr[0]

        time_left -= time_spent

    return None, None


def traceroute(dest_addr, max_hops=30, timeout=2, probes=3, resolve_dns=False):
    """
    Проводит трассировку маршрута до целевого узла.
    dest_addr может быть задан как IP-адрес или доменное имя.
    Если resolve_dns True, дополнительно производится обратное разрешение IP в имя хоста.
    """
    try:
        dest_ip = socket.gethostbyname(dest_addr)
    except socket.gaierror:
        print(f"Не удается разрешить адрес {dest_addr}")
        sys.exit(1)

    print(f"Трассировка до {dest_addr} ({dest_ip}) с максимальным количеством хопсов {max_hops}:")

    for ttl in range(1, max_hops + 1):
        print(f"{ttl:2}  ", end="")
        for probe in range(probes):
            sock = None  # Инициализируем переменную
            try:
                sock = socket.socket(socket.AF_INET, socket.SOCK_RAW, ICMP_PROTO)
                sock.settimeout(timeout)
                send_icmp(sock, dest_ip, ttl, ttl)  # Используем ttl в качестве sequence number
                delay, current_addr = receive_icmp(sock, timeout)
            except socket.error as e:
                print(f"\nОшибка при отправке пакета: {e}")
                sys.exit(1)
            finally:
                if sock is not None:
                    sock.close()
            if delay is None:
                print("* ", end="")
            else:
                if resolve_dns:
                    try:
                        host_name = socket.gethostbyaddr(current_addr)[0]
                    except socket.herror:
                        host_name = current_addr
                    print(f"{host_name} ({current_addr}) {delay * 1000:.2f} ms ", end="")
                else:
                    print(f"{current_addr} {delay * 1000:.2f} ms ", end="")
                if current_addr == dest_ip:
                    print("\nТрассировка завершена.")
                    return
        print()
    print("Трассировка завершена.")


def main():
    target = sys.argv[1]
    # Если передан параметр --resolve, разрешаем имена узлов
    resolve_flag = "--resolve" in sys.argv
    traceroute(target, resolve_dns=resolve_flag)


if __name__ == "__main__":
    main()