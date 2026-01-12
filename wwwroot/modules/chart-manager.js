/**
 * Chart Manager Module
 * Handles Chart.js chart initialization and updates
 */
class ChartManager {
    constructor() {
        this.connectionsChart = null;
        this.memoryChart = null;
    }

    /**
     * Get default chart options
     */
    getDefaultOptions() {
        return {
            responsive: true,
            maintainAspectRatio: false,
            animation: { duration: 300 },
            scales: {
                x: {
                    type: 'category',
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#9ca3af', maxTicksLimit: 8 }
                },
                y: {
                    beginAtZero: true,
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#9ca3af' }
                }
            },
            plugins: {
                legend: {
                    labels: { color: '#9ca3af', usePointStyle: true }
                }
            }
        };
    }

    /**
     * Initialize all charts
     */
    init() {
        this.initConnectionsChart();
        this.initMemoryChart();
    }

    /**
     * Initialize connections & blocking chart
     */
    initConnectionsChart() {
        const ctx = document.getElementById('connectionsChart');
        if (!ctx) return;

        this.connectionsChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: [],
                datasets: [
                    {
                        label: 'Connections',
                        data: [],
                        borderColor: '#3b82f6',
                        backgroundColor: 'rgba(59, 130, 246, 0.1)',
                        tension: 0.3,
                        fill: true
                    },
                    {
                        label: 'Blocked',
                        data: [],
                        borderColor: '#ef4444',
                        backgroundColor: 'rgba(239, 68, 68, 0.1)',
                        tension: 0.3,
                        fill: true
                    }
                ]
            },
            options: this.getDefaultOptions()
        });
    }

    /**
     * Initialize memory chart
     */
    initMemoryChart() {
        const ctx = document.getElementById('memoryChart');
        if (!ctx) return;

        this.memoryChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Memory (MB)',
                    data: [],
                    borderColor: '#10b981',
                    backgroundColor: 'rgba(16, 185, 129, 0.1)',
                    tension: 0.3,
                    fill: true
                }]
            },
            options: this.getDefaultOptions()
        });
    }

    /**
     * Update charts with new data
     */
    updateCharts(data, useFullDate = false) {
        if (!data || data.length === 0) return;

        const labels = data.map(d => this.formatTimestamp(d.timestamp, useFullDate));

        this.updateConnectionsChart(labels, data);
        this.updateMemoryChart(labels, data);
    }

    /**
     * Format timestamp for chart labels
     */
    formatTimestamp(timestamp, useFullDate = false) {
        const date = new Date(timestamp);

        if (useFullDate) {
            return date.toLocaleString('en-US', {
                hour12: false,
                month: 'short',
                day: 'numeric',
                hour: '2-digit',
                minute: '2-digit'
            });
        }

        return date.toLocaleTimeString('en-US', {
            hour12: false,
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
    }

    /**
     * Update connections chart
     */
    updateConnectionsChart(labels, data) {
        if (!this.connectionsChart) return;

        this.connectionsChart.data.labels = labels;
        this.connectionsChart.data.datasets[0].data = data.map(d => d.connections);
        this.connectionsChart.data.datasets[1].data = data.map(d => d.blocked);
        this.connectionsChart.update('none');
    }

    /**
     * Update memory chart
     */
    updateMemoryChart(labels, data) {
        if (!this.memoryChart) return;

        this.memoryChart.data.labels = labels;
        this.memoryChart.data.datasets[0].data = data.map(d => d.memory);
        this.memoryChart.update('none');
    }

    /**
     * Destroy all charts
     */
    destroy() {
        if (this.connectionsChart) {
            this.connectionsChart.destroy();
            this.connectionsChart = null;
        }
        if (this.memoryChart) {
            this.memoryChart.destroy();
            this.memoryChart = null;
        }
    }
}

// Export singleton instance
window.chartManager = new ChartManager();
