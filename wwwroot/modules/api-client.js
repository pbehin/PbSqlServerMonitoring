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

        return response.json();
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

    // API Endpoints

    async getServerHealth() {
        return this.get('/api/health');
    }

    async getMetricsHistory(rangeSeconds, page = 1, pageSize = 100) {
        return this.get('/api/metrics/history', { rangeSeconds, page, pageSize });
    }

    async getMetricsHistoryByRange(from, to, page = 1, pageSize = 100) {
        return this.get('/api/metrics/history/range', { from, to, page, pageSize });
    }

    async getLatestMetric() {
        return this.get('/api/metrics/latest');
    }

    async getBufferHealth() {
        return this.get('/api/metrics/buffer-health');
    }

    async getBlockingHistory(rangeSeconds) {
        return this.get('/api/metrics/blocking-history', { rangeSeconds });
    }

    async getTopCpuQueries(top = 25, page = 1, pageSize = 25) {
        return this.get('/api/queries/top-cpu', { top, page, pageSize });
    }

    async getActiveCpuQueries(top = 5) {
        return this.get('/api/queries/active-cpu', { top, _: Date.now() });
    }

    async getQueryHistory(hours = 24, sortBy = 'cpu', page = 1, pageSize = 50) {
        return this.get('/api/queries/history', { hours, sortBy, page, pageSize });
    }

    async getTopIoQueries(top = 25, page = 1, pageSize = 25) {
        return this.get('/api/queries/top-io', { top, page, pageSize });
    }

    async getSlowestQueries(top = 25, page = 1, pageSize = 25) {
        return this.get('/api/queries/slowest', { top, page, pageSize });
    }

    async getMissingIndexes(top = 50, page = 1, pageSize = 25) {
        return this.get('/api/indexes/missing', { top, page, pageSize });
    }

    async getBlockingSessions() {
        return this.get('/api/blocking/active');
    }

    async getCurrentLocks(page = 1, pageSize = 50) {
        return this.get('/api/locks/current', { page, pageSize });
    }

    async getRunningQueries(page = 1, pageSize = 50) {
        return this.get('/api/running/active', { page, pageSize });
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

// Export singleton instance
window.apiClient = new ApiClient();
