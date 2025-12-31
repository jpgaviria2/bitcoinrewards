// Main application logic

class RewardsDisplayApp {
    constructor() {
        this.config = new DisplayConfig();
        this.connection = null;
        this.currentTimer = null;
        this.screens = {
            config: document.getElementById('config-screen'),
            waiting: document.getElementById('waiting-screen'),
            display: document.getElementById('display-screen')
        };
        
        this.init();
    }

    init() {
        // Set up event listeners
        document.getElementById('config-form').addEventListener('submit', (e) => {
            e.preventDefault();
            this.handleConfigSubmit();
        });

        document.getElementById('btn-settings').addEventListener('click', () => {
            this.disconnect();
            this.showScreen('config');
        });

        // Check if we have a saved configuration
        if (this.config.hasConfig()) {
            this.showScreen('waiting');
            this.connect();
        } else {
            this.showScreen('config');
        }
    }

    showScreen(screenName) {
        Object.values(this.screens).forEach(screen => {
            screen.classList.remove('active');
        });
        this.screens[screenName].classList.add('active');
    }

    handleConfigSubmit() {
        const serverUrl = document.getElementById('server-url').value.trim();
        const storeId = document.getElementById('store-id').value.trim();

        if (!serverUrl || !storeId) {
            this.showConfigStatus('Please fill in all fields', 'error');
            return;
        }

        this.config.saveConfig({ serverUrl, storeId });
        this.showConfigStatus('Configuration saved! Connecting...', 'success');

        setTimeout(() => {
            this.showScreen('waiting');
            this.connect();
        }, 1000);
    }

    showConfigStatus(message, type) {
        const status = document.getElementById('config-status');
        status.textContent = message;
        status.className = `config-status ${type}`;
    }

    async connect() {
        const hubUrl = this.config.getHubUrl();
        const storeId = this.config.getStoreId();

        if (!hubUrl || !storeId) {
            console.error('Invalid configuration');
            return;
        }

        try {
            // Create SignalR connection
            this.connection = new signalR.HubConnectionBuilder()
                .withUrl(hubUrl)
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: (retryContext) => {
                        // Exponential backoff: 0s, 2s, 10s, 30s
                        if (retryContext.previousRetryCount === 0) return 0;
                        if (retryContext.previousRetryCount === 1) return 2000;
                        if (retryContext.previousRetryCount === 2) return 10000;
                        return 30000;
                    }
                })
                .build();

            // Set up event handlers
            this.connection.on('ReceiveReward', (reward) => {
                this.handleReward(reward);
            });

            this.connection.onreconnecting(() => {
                this.updateConnectionStatus(false);
            });

            this.connection.onreconnected(() => {
                this.updateConnectionStatus(true);
                this.joinStore();
            });

            this.connection.onclose(() => {
                this.updateConnectionStatus(false);
            });

            // Start connection
            await this.connection.start();
            console.log('SignalR connected');
            
            // Join the store group
            await this.joinStore();
            
            this.updateConnectionStatus(true);
        } catch (error) {
            console.error('Connection error:', error);
            this.updateConnectionStatus(false);
            
            // Retry after delay
            setTimeout(() => this.connect(), 5000);
        }
    }

    async joinStore() {
        const storeId = this.config.getStoreId();
        if (this.connection && storeId) {
            try {
                await this.connection.invoke('JoinStore', storeId);
                console.log('Joined store:', storeId);
            } catch (error) {
                console.error('Failed to join store:', error);
            }
        }
    }

    disconnect() {
        if (this.connection) {
            this.connection.stop();
            this.connection = null;
        }
        this.clearCurrentReward();
    }

    updateConnectionStatus(connected) {
        const statusText = document.getElementById('connection-status');
        const dot = document.querySelector('.connection-dot');
        
        if (connected) {
            statusText.textContent = 'Connected';
            dot.classList.add('connected');
        } else {
            statusText.textContent = 'Disconnected';
            dot.classList.remove('connected');
        }
    }

    handleReward(reward) {
        console.log('Received reward:', reward);
        
        // Clear any existing timer
        this.clearCurrentReward();

        // Display the reward
        this.displayReward(reward);

        // Set timer to clear after duration
        const duration = reward.DisplayDurationSeconds || 60;
        this.startTimer(duration);
    }

    displayReward(reward) {
        // Show display screen
        this.showScreen('display');

        // Update amounts
        document.getElementById('sats-amount').textContent = reward.RewardSatoshis.toLocaleString();
        const btcAmount = (reward.RewardSatoshis / 100000000).toFixed(8);
        document.getElementById('btc-amount').textContent = btcAmount;

        // Generate QR code
        const qrContainer = document.getElementById('qr-code');
        qrContainer.innerHTML = ''; // Clear previous QR code
        
        QRCode.toCanvas(reward.ClaimLink, {
            width: 300,
            margin: 2,
            color: {
                dark: '#000000',
                light: '#FFFFFF'
            }
        }, (error, canvas) => {
            if (error) {
                console.error('QR code generation error:', error);
                qrContainer.innerHTML = '<p>Failed to generate QR code</p>';
            } else {
                qrContainer.appendChild(canvas);
            }
        });

        // Update claim URL
        const claimUrl = new URL(reward.ClaimLink);
        document.getElementById('claim-url-short').textContent = claimUrl.hostname + '...' + claimUrl.pathname.slice(-8);
    }

    startTimer(duration) {
        const timerText = document.getElementById('timer-text');
        const timerProgress = document.getElementById('timer-progress');
        const circumference = 2 * Math.PI * 45; // radius = 45
        
        timerProgress.style.strokeDasharray = circumference;
        timerProgress.style.strokeDashoffset = 0;

        let remaining = duration;
        timerText.textContent = remaining;

        this.currentTimer = setInterval(() => {
            remaining--;
            timerText.textContent = remaining;

            // Update progress circle
            const offset = circumference - (remaining / duration) * circumference;
            timerProgress.style.strokeDashoffset = offset;

            if (remaining <= 0) {
                this.clearCurrentReward();
            }
        }, 1000);
    }

    clearCurrentReward() {
        if (this.currentTimer) {
            clearInterval(this.currentTimer);
            this.currentTimer = null;
        }

        // Return to waiting screen
        if (this.screens.display.classList.contains('active')) {
            this.showScreen('waiting');
        }
    }
}

// Initialize app when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.app = new RewardsDisplayApp();
    });
} else {
    window.app = new RewardsDisplayApp();
}

