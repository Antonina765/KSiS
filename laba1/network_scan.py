from scapy.all import ARP, Ether, srp
import socket

# проход по интерфейсам сетевым
#     проход по ip этой сети
#          arp запрос на этот ip
#          если ok, то показать инфу об устройстве

import psutil
import ipaddress
import concurrent.futures


def get_device_info(ip):
    arp = ARP(pdst=ip)
    ether = Ether(dst="ff:ff:ff:ff:ff:ff")
    packet = ether / arp

    result = srp(packet, timeout=3, verbose=0)[0]

    if result:
        for sent, received in result:
            mac = received.hwsrc
            try:
                hostname = socket.gethostbyaddr(received.psrc)[0]
            except:
                hostname = "Unknown"

            print(f"IP: {received.psrc}, MAC: {mac}, Hostname: {hostname}")
    else:
        print(f"No device found at IP: {ip}")


for interface, addrs in psutil.net_if_addrs().items():
    print(f"Interface: {interface}")
    for addr in addrs:
        if addr.family == socket.AF_INET:
            print(f"   IP адрес: {addr.address}")
            print(f"   Маска сети: {addr.netmask}")

            ip_parts = addr.address.split('.')
            netmask_parts = addr.netmask.split('.')

            network_parts = []
            for i in range(4):
                # Вычисляем каждую часть IP-адреса сети
                network_parts.append(str(int(ip_parts[i]) & int(netmask_parts[i])))

                # Объединяем части IP-адреса сети
                network_address = '.'.join(network_parts)
            print(f"   IP адрес сети: {network_address}")

            # Извлечение диапазона адресов из IP адреса сети и маски подсети
            # тут короче смотря как у тебя сети в каком порядке идут (в ipconfig) то можешь для проверки
            # написать вместо network_address свой IP адрес и вместо addr.netmask свою маску
            network = ipaddress.IPv4Network(f'{"172.20.10.13"}/{"255.255.255.240"}', strict=False)
            ip_list = [str(ip) for ip in network.hosts()]

            with concurrent.futures.ThreadPoolExecutor() as executor:
                executor.map(get_device_info, ip_list)