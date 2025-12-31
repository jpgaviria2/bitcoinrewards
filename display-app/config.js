// Configuration management using URL parameters and localStorage

class DisplayConfig {
    constructor() {
        this.storageKey = 'btcpay-rewards-display-config';
        this.config = this.loadConfig();
    }

    loadConfig() {
        // First, try to load from URL parameters
        const urlParams = new URLSearchParams(window.location.search);
        const serverUrl = urlParams.get('server');
        const storeId = urlParams.get('store');

        if (serverUrl && storeId) {
            const config = {
                serverUrl: serverUrl,
                storeId: storeId
            };
            this.saveConfig(config);
            return config;
        }

        // Fall back to localStorage
        const stored = localStorage.getItem(this.storageKey);
        if (stored) {
            try {
                return JSON.parse(stored);
            } catch (e) {
                console.error('Failed to parse stored config:', e);
            }
        }

        return null;
    }

    saveConfig(config) {
        this.config = config;
        localStorage.setItem(this.storageKey, JSON.stringify(config));
    }

    clearConfig() {
        this.config = null;
        localStorage.removeItem(this.storageKey);
    }

    hasConfig() {
        return this.config !== null && 
               this.config.serverUrl && 
               this.config.storeId;
    }

    getHubUrl() {
        if (!this.hasConfig()) return null;
        const baseUrl = this.config.serverUrl.replace(/\/$/, '');
        return `${baseUrl}/plugins/bitcoin-rewards/hubs/display`;
    }

    getStoreId() {
        return this.config?.storeId || null;
    }

    getServerUrl() {
        return this.config?.serverUrl || null;
    }
}

// Export for use in app.js
window.DisplayConfig = DisplayConfig;

