document.addEventListener('DOMContentLoaded', function() {
    const chatWindow = document.getElementById('chatWindow');
    const messageInput = document.getElementById('messageInput');
    const sendButton = document.getElementById('sendButton');

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

    sendButton.addEventListener('click', sendMessage);
    messageInput.addEventListener('keyup', function(e) {
        if (e.key === 'Enter') sendMessage();
    });

    // Каждые 2 секунды запрашиваем новые сообщения
    setInterval(fetchMessages, 2000);
});
