/**
 * API Client Module
 * Handles all API communication with the backend
 */
class ApiClient {
    constructor() {
        this.baseUrl = '';
        this.apiKey = null;
    }

    /**
     * Set API key for authenticated requests
     */
    setApiKey(key) {
        this.apiKey = key;
    }

    /**
     * Build headers including API key if set
     */
    getHeaders() {
        const headers = {
            'Content-Type': 'application/json'
        };
        if (this.apiKey) {
            headers['X-API-Key'] = this.apiKey;
        }
        return headers;
    }

    /**
     * Make a GET request
     */
    async get(endpoint, params = {}) {
        const url = new URL(endpoint, window.location.origin);
        Object.entries(params).forEach(([key, value]) => {
            if (value !== undefined && value !== null) {
                url.searchParams.append(key, value);
            }
        });

        const response = await fetch(url.toString(), {
            method: 'GET',
            headers: this.getHeaders()
        });

        if (!response.ok) {
            throw new Error(`API error: ${response.status} ${response.statusText}`);
        }

        return response.json();
    }

    /**
     * Make a POST request
     */
    async post(endpoint, data) {
        const response = await fetch(endpoint, {
            method: 'POST',
            headers: this.getHeaders(),
            body: JSON.stringify(data)
        });

        const result = await response.json();

        if (!response.ok) {
            const errorMessage = result.error || result.message || `API error: ${response.status} ${response.statusText}`;
            const error = new Error(errorMessage);
            error.status = response.status;
            error.details = result.details;
            error.requiresEmailConfirmation = result.requiresEmailConfirmation;
            throw error;
        }

        return result;
    }

    /**
     * Make a DELETE request
     */
    async delete(endpoint) {
        const response = await fetch(endpoint, {
            method: 'DELETE',
            headers: this.getHeaders()
        });

        if (!response.ok) {
            throw new Error(`API error: ${response.status} ${response.statusText}`);
        }

        return response.ok;
    }

    async getServerHealth() {
        return this.get('/api/health');
    }

    async checkServerConnection(connectionId) {
        return this.get('/api/health', { connectionId });
    }

    async getConnectionSettings() {
        return this.get('/api/settings/connection');
    }

    async testConnection(data) {
        return this.post('/api/settings/connection/test', data);
    }

    async saveConnection(data) {
        return this.post('/api/settings/connection', data);
    }

    async clearConnection() {
        return this.delete('/api/settings/connection');
    }
}

window.apiClient = new ApiClient();
