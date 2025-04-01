document.addEventListener('DOMContentLoaded', function() {
    const chatWindow = document.getElementById('chatWindow');
    const messageInput = document.getElementById('messageInput');
    const sendButton = document.getElementById('sendButton');
    const stopServerButton = document.getElementById('stopServerButton');

    // Функция для получения сообщений
    async function fetchMessages() {
        try {
            const response = await fetch('/Chat/GetMessages');
            const messages = await response.json();
            messages.forEach(msg => {
                const p = document.createElement('p');
                p.textContent = msg;
                chatWindow.appendChild(p);
            });
            chatWindow.scrollTop = chatWindow.scrollHeight;
        } catch (error) {
            console.error('Ошибка получения сообщений', error);
        }
    }

    // Функция для отправки сообщений
    async function sendMessage() {
        const message = messageInput.value;
        if (!message) return;
        try {
            await fetch('/Chat/SendMessage', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded'
                },
                body: new URLSearchParams({ message: message })
            });
            messageInput.value = '';
        } catch (error) {
            console.error('Ошибка отправки сообщения', error);
        }
    }

    // Обработчик для отправки сообщения
    sendButton.addEventListener('click', sendMessage);
    messageInput.addEventListener('keyup', function(e) {
        if (e.key === 'Enter') sendMessage();
    });

    // Запускаем опрос сообщений каждые 2 секунды
    const messageInterval = setInterval(fetchMessages, 2000);

    // Обработчик для остановки сервера: при успешном завершении отключает ввод и остановку опроса
    stopServerButton.addEventListener('click', async function() {
        try {
            const response = await fetch('/Chat/StopServer', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            });
            const result = await response.json();
            if (result.success) {
                // Останавливаем опрос сообщений
                clearInterval(messageInterval);
                // Деактивируем кнопки и поле ввода
                sendButton.disabled = true;
                messageInput.disabled = true;
                stopServerButton.disabled = true;
                alert(result.message);
            } else {
                alert("Ошибка при остановке сервера.");
            }
        } catch (error) {
            console.error('Ошибка остановки сервера', error);
            alert('Ошибка при остановке сервера.');
        }
    });
});
