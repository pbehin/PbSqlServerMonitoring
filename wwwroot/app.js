/**
 * SQL Server Monitoring Dashboard - JavaScript Application
 * Uses modular architecture with consolidated polling.
 */

class SqlMonitorApp {
    constructor() {
        this.currentSection = 'dashboard';
        this.currentQueryTab = 'cpu';

        // Consolidated polling - single interval for all dashboard data
        this.pollingInterval = null;
        this.pollingDelay = 3000; // 3 seconds

        // Chart instances (now managed by chartManager module)
        this.connectionsChart = null;
        this.memoryChart = null;

        // Data cache and sort state (now delegated to tableManager module)
        this.dataCache = {};
        this.sortState = {
            queriesTable: { col: null, dir: 'desc' },
            topCpuTable: { col: null, dir: 'desc' },
            runningTable: { col: null, dir: 'desc' },
            indexesTable: { col: null, dir: 'desc' },
            blockingTable: { col: null, dir: 'desc' },
            blockingFullTable: { col: null, dir: 'desc' },
            locksTable: { col: null, dir: 'desc' },
            blockingHistoryTable: { col: 'time', dir: 'desc' }
        };

        this.init();
    }

    init() {
        this.bindEvents();
        this.loadConnectionSettings();
        this.initCharts();
        this.loadAllData();
        this.startConsolidatedPolling();
    }

    bindEvents() {
        // Navigation
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const section = item.dataset.section;
                this.navigateTo(section);
            });
        });

        // View All links
        document.querySelectorAll('[data-nav]').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                this.navigateTo(link.dataset.nav);
            });
        });

        // Blocking History
        document.getElementById('blockingHistoryBtn')?.addEventListener('click', () => {
            this.showBlockingHistory();
        });
        document.querySelector('.blocking-history-close')?.addEventListener('click', () => {
            document.getElementById('blockingHistoryModal').classList.remove('active');
        });

        // Refresh button
        document.getElementById('refreshBtn').addEventListener('click', async () => {
            const btn = document.getElementById('refreshBtn');
            btn.disabled = true;
            btn.classList.add('loading-btn');
            await this.loadAllData();
            btn.disabled = false;
            btn.classList.remove('loading-btn');
        });

        // Query tabs
        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                this.switchQueryTab(btn.dataset.tab);
            });
        });

        // Modal close
        document.querySelector('.modal-close').addEventListener('click', () => {
            this.closeModal();
        });

        document.querySelector('.modal-overlay').addEventListener('click', () => {
            this.closeModal();
        });

        // Copy query button
        document.getElementById('copyQuery').addEventListener('click', () => {
            const queryText = document.getElementById('queryDetail').textContent;
            navigator.clipboard.writeText(queryText).then(() => {
                const btn = document.getElementById('copyQuery');
                btn.textContent = 'Copied!';
                setTimeout(() => {
                    btn.textContent = 'Copy Query';
                }, 2000);
            });
        });

        // Keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                this.closeModal();
            }
        });

        // Connection status click - go to settings
        document.getElementById('connectionStatus').addEventListener('click', () => {
            this.navigateTo('settings');
        });
        document.getElementById('connectionStatus').classList.add('clickable');

        // Settings form - Windows Auth toggle
        document.getElementById('windowsAuthCheck').addEventListener('change', (e) => {
            const sqlAuthFields = document.getElementById('sqlAuthFields');
            if (e.target.checked) {
                sqlAuthFields.classList.add('hidden');
            } else {
                sqlAuthFields.classList.remove('hidden');
            }
        });

        // Test Connection button
        document.getElementById('testConnectionBtn').addEventListener('click', () => {
            this.testConnection();
        });

        // Save Connection form submit
        document.getElementById('connectionForm').addEventListener('submit', (e) => {
            e.preventDefault();
            this.saveConnection();
        });

        // Clear Connection button
        document.getElementById('clearConnectionBtn').addEventListener('click', () => {
            this.clearConnectionSettings();
        });

        // Chart time range selector
        document.getElementById('chartTimeRange').addEventListener('change', (e) => {
            const customInputs = document.getElementById('customRangeInputs');
            if (e.target.value === 'custom') {
                customInputs.style.display = 'flex';
                // Set default values to last hour
                const now = new Date();
                const hourAgo = new Date(now.getTime() - 3600000);
                document.getElementById('chartToDate').value = this.formatDateTimeLocal(now);
                document.getElementById('chartFromDate').value = this.formatDateTimeLocal(hourAgo);
            } else {
                customInputs.style.display = 'none';
                this.loadChartData();
            }
        });

        // Custom range apply button
        document.getElementById('applyCustomRange').addEventListener('click', () => {
            this.loadChartDataByRange();
        });

        // Query time range selector (affects all tabs)
        document.getElementById('queryHistoryRange')?.addEventListener('change', () => {
            this.loadQueries(this.currentQueryTab);
        });

        // Blocking time range selector
        document.getElementById('blockingHistoryRange')?.addEventListener('change', () => {
            this.loadBlockingSessions(true);
        });

        // Locks time range selector
        document.getElementById('locksHistoryRange')?.addEventListener('change', () => {
            this.loadLocks();
        });
    }

    navigateTo(section) {
        this.currentSection = section;

        // Update nav
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.section === section);
        });

        // Update sections
        document.querySelectorAll('.content-section').forEach(sec => {
            sec.classList.toggle('active', sec.id === section);
        });

        // Update header
        const titles = {
            'dashboard': ['Dashboard', 'Real-time SQL Server performance monitoring'],
            'running': ['Running Queries', 'Currently executing queries on the server'],
            'queries': ['Query Performance', 'Analyze CPU, IO, and execution time of queries'],
            'indexes': ['Missing Indexes', 'Index recommendations to improve performance'],
            'blocking': ['Blocking Sessions', 'Monitor active blocking chains'],
            'locks': ['Lock Analysis', 'Current locks and wait statistics'],
            'settings': ['Settings', 'Configure SQL Server connection']
        };

        if (titles[section]) {
            document.getElementById('pageTitle').textContent = titles[section][0];
            document.getElementById('pageSubtitle').textContent = titles[section][1];
        }

        // Load section-specific data
        this.loadSectionData(section);
    }

    loadSectionData(section) {
        switch (section) {
            case 'running':
                this.loadRunningQueries();
                break;
            case 'queries':
                this.loadQueries(this.currentQueryTab);
                break;
            case 'indexes':
                this.loadMissingIndexes();
                break;
            case 'blocking':
                this.loadBlockingSessions(true);
                break;
            case 'locks':
                this.loadLocks();
                break;
            case 'settings':
                this.loadConnectionSettings();
                break;
        }
    }

    switchQueryTab(tab) {
        this.currentQueryTab = tab;

        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.tab === tab);
        });

        this.loadQueries(tab);
    }

    async loadAllData() {
        const tasks = [
            this.loadServerHealth()
        ];

        // Always load dashboard widgets, but use appropriate view mode for blocking
        tasks.push(this.loadTopCpuQueries());

        // Auto-refresh only: Dashboard + Running Queries
        // Other sections (Queries, Blocking, Locks, Indexes) are historical/on-demand
        if (this.currentSection === 'running') tasks.push(this.loadRunningQueries());

        await Promise.all(tasks);

        this.updateLastRefreshTime();
    }

    updateLastRefreshTime() {
        const now = new Date();
        document.getElementById('lastUpdated').textContent = now.toLocaleTimeString();
    }

    // ========== Performance Charts ==========

    initCharts() {
        const chartOptions = {
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

        // Connections & Blocking Chart
        const connectionsCtx = document.getElementById('connectionsChart');
        if (connectionsCtx) {
            this.connectionsChart = new Chart(connectionsCtx, {
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
                options: chartOptions
            });
        }

        // Memory Chart
        const memoryCtx = document.getElementById('memoryChart');
        if (memoryCtx) {
            this.memoryChart = new Chart(memoryCtx, {
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
                options: chartOptions
            });
        }
    }

    /**
     * Consolidated polling - single interval for all dashboard updates
     * Combines health, charts, and running queries into one fetch cycle
     */
    startConsolidatedPolling() {
        // Initial loads
        this.loadChartData();

        // Single polling interval for dashboard + charts + running queries
        this.pollingInterval = setInterval(async () => {
            // Load all dashboard data including charts in parallel
            await Promise.all([
                this.loadAllData(),
                this.loadChartData()
            ]);
        }, this.pollingDelay);
    }

    /**
     * Stop polling (useful for cleanup)
     */
    stopPolling() {
        if (this.pollingInterval) {
            clearInterval(this.pollingInterval);
            this.pollingInterval = null;
        }
    }

    async loadChartData() {
        const timeRange = document.getElementById('chartTimeRange')?.value || 60;

        try {
            const response = await fetch(`/api/metrics/history?rangeSeconds=${timeRange}&pageSize=1000`);
            const result = await response.json();

            // Handle paginated response (new format) or array (legacy)
            const data = result.items || result;

            if (!data || data.length === 0) return;

            // Format timestamps
            const labels = data.map(d => {
                const date = new Date(d.timestamp);
                return date.toLocaleTimeString('en-US', {
                    hour12: false,
                    hour: '2-digit',
                    minute: '2-digit',
                    second: '2-digit'
                });
            });

            // Update connections chart
            if (this.connectionsChart) {
                this.connectionsChart.data.labels = labels;
                this.connectionsChart.data.datasets[0].data = data.map(d => d.connections);
                this.connectionsChart.data.datasets[1].data = data.map(d => d.blocked);
                this.connectionsChart.update('none');
            }

            // Update memory chart
            if (this.memoryChart) {
                this.memoryChart.data.labels = labels;
                this.memoryChart.data.datasets[0].data = data.map(d => d.memory);
                this.memoryChart.update('none');
            }
        } catch (error) {
            console.error('Failed to load chart data:', error);
        }
    }

    async loadChartDataByRange() {
        const fromDate = document.getElementById('chartFromDate').value;
        const toDate = document.getElementById('chartToDate').value;

        if (!fromDate || !toDate) {
            console.error('Please select both from and to dates');
            return;
        }

        try {
            const fromISO = new Date(fromDate).toISOString();
            const toISO = new Date(toDate).toISOString();

            const response = await fetch(`/api/metrics/history/range?from=${fromISO}&to=${toISO}&pageSize=1000`);
            const result = await response.json();

            // Handle paginated response (new format) or array (legacy)
            const data = result.items || result;

            if (!data || data.length === 0) return;

            // Format timestamps (include date for longer ranges)
            const labels = data.map(d => {
                const date = new Date(d.timestamp);
                return date.toLocaleString('en-US', {
                    hour12: false,
                    month: 'short',
                    day: 'numeric',
                    hour: '2-digit',
                    minute: '2-digit'
                });
            });

            // Update charts
            if (this.connectionsChart) {
                this.connectionsChart.data.labels = labels;
                this.connectionsChart.data.datasets[0].data = data.map(d => d.connections);
                this.connectionsChart.data.datasets[1].data = data.map(d => d.blocked);
                this.connectionsChart.update('none');
            }

            if (this.memoryChart) {
                this.memoryChart.data.labels = labels;
                this.memoryChart.data.datasets[0].data = data.map(d => d.memory);
                this.memoryChart.update('none');
            }
        } catch (error) {
            console.error('Failed to load chart data by range:', error);
        }
    }

    formatDateTimeLocal(date) {
        const pad = n => n.toString().padStart(2, '0');
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }

    // ========== Connection Settings ==========

    async loadConnectionSettings() {
        // First try to load from sessionStorage (persists during tab session)
        const savedSettings = sessionStorage.getItem('sqlConnectionSettings');

        if (savedSettings) {
            try {
                const data = JSON.parse(savedSettings);
                this.populateConnectionForm(data);

                // Auto-connect if we have saved settings
                const autoConnect = sessionStorage.getItem('sqlAutoConnect');
                if (autoConnect === 'true') {
                    this.saveConnection();
                }
                return;
            } catch (e) {
                console.log('Could not parse saved settings');
            }
        }

        // Fall back to server-side settings
        try {
            const response = await fetch('/api/settings/connection');
            const data = await response.json();

            if (data.configured) {
                this.populateConnectionForm(data);
            }
        } catch (error) {
            console.log('Could not load connection settings');
        }
    }

    populateConnectionForm(data) {
        document.getElementById('serverInput').value = data.server || '';
        document.getElementById('databaseInput').value = data.database || 'master';
        document.getElementById('windowsAuthCheck').checked = data.useWindowsAuth || false;
        document.getElementById('usernameInput').value = data.username || '';
        document.getElementById('trustCertCheck').checked = data.trustCertificate !== false;
        document.getElementById('timeoutInput').value = data.timeout || 30;

        // Toggle SQL auth fields
        const sqlAuthFields = document.getElementById('sqlAuthFields');
        if (data.useWindowsAuth) {
            sqlAuthFields.classList.add('hidden');
        } else {
            sqlAuthFields.classList.remove('hidden');
        }
    }

    saveToSessionStorage() {
        // Save form values (except password) to sessionStorage
        const data = {
            server: document.getElementById('serverInput').value.trim(),
            database: document.getElementById('databaseInput').value.trim() || 'master',
            useWindowsAuth: document.getElementById('windowsAuthCheck').checked,
            username: document.getElementById('usernameInput').value.trim(),
            trustCertificate: document.getElementById('trustCertCheck').checked,
            timeout: parseInt(document.getElementById('timeoutInput').value) || 30
        };
        sessionStorage.setItem('sqlConnectionSettings', JSON.stringify(data));
        sessionStorage.setItem('sqlAutoConnect', 'true');
    }

    clearConnectionSettings() {
        sessionStorage.removeItem('sqlConnectionSettings');
        sessionStorage.removeItem('sqlAutoConnect');

        // Clear form
        document.getElementById('serverInput').value = '';
        document.getElementById('databaseInput').value = 'master';
        document.getElementById('windowsAuthCheck').checked = false;
        document.getElementById('usernameInput').value = '';
        document.getElementById('passwordInput').value = '';
        document.getElementById('trustCertCheck').checked = true;
        document.getElementById('timeoutInput').value = '30';
        document.getElementById('sqlAuthFields').classList.remove('hidden');

        // Clear server-side connection
        fetch('/api/settings/connection', { method: 'DELETE' }).catch(() => { });

        this.showConnectionResult(true, 'Connection settings cleared');
    }

    /**
     * Security: Clear password field from DOM to prevent it from lingering in memory
     */
    clearPasswordField() {
        const passwordInput = document.getElementById('passwordInput');
        if (passwordInput) {
            passwordInput.value = '';
        }
    }

    getConnectionFormData() {
        return {
            server: document.getElementById('serverInput').value.trim(),
            database: document.getElementById('databaseInput').value.trim() || 'master',
            useWindowsAuth: document.getElementById('windowsAuthCheck').checked,
            username: document.getElementById('usernameInput').value.trim(),
            password: document.getElementById('passwordInput').value,
            trustCertificate: document.getElementById('trustCertCheck').checked,
            timeout: parseInt(document.getElementById('timeoutInput').value) || 30
        };
    }

    validateConnectionData(data) {
        if (!data.server) {
            this.showConnectionResult(false, 'Please enter a server name');
            return false;
        }
        if (!data.useWindowsAuth) {
            if (!data.username) {
                this.showConnectionResult(false, 'Please enter a username');
                return false;
            }
            if (!data.password) {
                this.showConnectionResult(false, 'Please enter a password');
                return false;
            }
        }
        return true;
    }

    async testConnection() {
        const btn = document.getElementById('testConnectionBtn');
        const btnText = btn.querySelector('.btn-text');
        const spinner = btn.querySelector('.loading');
        const resultDiv = document.getElementById('connectionResult');

        // Validate
        const data = this.getConnectionFormData();
        if (!this.validateConnectionData(data)) return;

        // Show loading
        btnText.textContent = 'Testing...';
        spinner.style.display = 'inline-block';
        btn.disabled = true;

        try {
            const response = await fetch('/api/settings/connection/test', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });

            const result = await response.json();
            this.showConnectionResult(result.success, result.message, result.serverVersion);
        } catch (error) {
            this.showConnectionResult(false, 'Failed to test connection: ' + error.message);
        } finally {
            btnText.textContent = 'Test Connection';
            spinner.style.display = 'none';
            btn.disabled = false;
        }
    }

    async saveConnection() {
        const btn = document.getElementById('saveConnectionBtn');
        const btnText = btn.querySelector('.btn-text');
        const spinner = btn.querySelector('.loading');

        // Validate
        const data = this.getConnectionFormData();
        if (!this.validateConnectionData(data)) return;

        // Show loading
        btnText.textContent = 'Connecting...';
        spinner.style.display = 'inline-block';
        btn.disabled = true;

        try {
            const response = await fetch('/api/settings/connection', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });

            const result = await response.json();

            if (result.success) {
                // Save to sessionStorage for page refresh
                this.saveToSessionStorage();

                // Security: Clear password from DOM after successful connection
                this.clearPasswordField();

                this.showConnectionResult(true, 'Connected successfully!', result.serverVersion);
                // Reload all data with new connection
                setTimeout(() => {
                    this.loadAllData();
                    this.navigateTo('dashboard');
                }, 1500);
            } else {
                this.showConnectionResult(false, result.message);
            }
        } catch (error) {
            this.showConnectionResult(false, 'Failed to save connection: ' + error.message);
        } finally {
            btnText.textContent = 'Save & Connect';
            spinner.style.display = 'none';
            btn.disabled = false;
        }
    }

    showConnectionResult(success, message, serverVersion = null) {
        const resultDiv = document.getElementById('connectionResult');
        resultDiv.style.display = 'block';
        resultDiv.className = 'connection-result ' + (success ? 'success' : 'error');

        let html = message;
        if (success && serverVersion) {
            html += `<span class="server-version">${serverVersion}</span>`;
        }
        resultDiv.innerHTML = html;
    }

    // ========== Server Health ==========

    async loadServerHealth() {
        const connectionBanner = document.getElementById('connectionBanner');

        try {
            const response = await fetch('/api/health');
            const data = await response.json();

            const statusDot = document.querySelector('.status-dot');
            const statusText = document.querySelector('.status-text');

            if (data.isConnected) {
                // Hide banner when connected
                connectionBanner.style.display = 'none';

                statusDot.className = 'status-dot connected';
                statusText.textContent = 'Connected';

                document.getElementById('activeConnections').textContent = this.formatNumber(data.activeConnections);
                document.getElementById('blockedProcesses').textContent = this.formatNumber(data.blockedProcesses);
                document.getElementById('cpuUsage').textContent = data.cpuUsagePercent.toFixed(1) + '%';
                document.getElementById('memoryUsage').textContent = this.formatNumber(data.memoryUsedMb);

                document.getElementById('serverName').textContent = data.serverName || '-';
                document.getElementById('sqlVersion').textContent = data.sqlServerVersion || '-';
                document.getElementById('edition').textContent = data.edition || '-';
                document.getElementById('uptime').textContent = this.formatUptime(data.uptime);
                document.getElementById('bufferHitRatio').textContent = data.bufferCacheHitRatio + '%';

                // Highlight blocked processes if any
                const blockedCard = document.getElementById('blockedProcesses').closest('.stat-card');
                if (data.blockedProcesses > 0) {
                    blockedCard.style.borderColor = 'var(--color-danger)';
                } else {
                    blockedCard.style.borderColor = 'var(--color-border-light)';
                }
            } else {
                // Show banner when not connected
                connectionBanner.style.display = 'block';

                statusDot.className = 'status-dot error';
                statusText.textContent = 'Not Connected';

                // Show helpful message if not configured
                if (data.errorMessage && data.errorMessage.includes('No connection string')) {
                    statusText.textContent = 'Configure';
                }
            }
        } catch (error) {
            console.error('Failed to load server health:', error);
            // Show banner on error too
            connectionBanner.style.display = 'block';

            const statusDot = document.querySelector('.status-dot');
            const statusText = document.querySelector('.status-text');
            statusDot.className = 'status-dot error';
            statusText.textContent = 'Error';
        }
    }

    async loadTopCpuQueries() {
        const tbody = document.querySelector('#topCpuTable tbody');

        // Only show loading initial state if table is empty or has error/empty message
        const hasData = tbody.querySelector('tr') && !tbody.querySelector('.loading-row') && !tbody.querySelector('.empty-state');
        if (!hasData) {
            tbody.innerHTML = '<tr><td colspan="3" class="loading-row"><span class="loading"></span> Loading...</td></tr>';
        }

        try {
            const response = await fetch('/api/queries/active-cpu?top=5&_=' + Date.now());
            const data = await response.json();

            this.dataCache.topCpu = data;
            this.renderTopCpuTable(data);

        } catch (error) {
            console.error('Failed to load top CPU queries:', error);
            if (!hasData) {
                tbody.innerHTML = '<tr><td colspan="3" class="empty-state">Failed to load data</td></tr>';
            }
        }
    }

    renderTopCpuTable(data) {
        const tbody = document.querySelector('#topCpuTable tbody');
        const state = this.sortState.topCpuTable;
        const sortedData = this.sortData(data, state.col, state.dir);

        tbody.innerHTML = '';

        if (!sortedData || sortedData.length === 0) {
            tbody.innerHTML = '<tr><td colspan="3" class="empty-state">No query data available</td></tr>';
            return;
        }

        sortedData.forEach(query => {
            const row = document.createElement('tr');
            row.innerHTML = `
                <td class="query-text" title="${this.escapeHtml(query.queryText)}">${this.truncateText(query.queryText, 50)}</td>
                <td class="number">${this.formatNumber(Math.round(query.avgCpuTimeMs))}</td>
                <td class="number">${this.formatNumber(query.executionCount)}</td>
            `;
            row.querySelector('.query-text').addEventListener('click', () => {
                this.showQueryModal(query.queryText);
            });
            tbody.appendChild(row);
        });
    }

    // ========== Running Queries ==========

    async loadRunningQueries() {
        const loadingEl = document.getElementById('runningLoading');
        const countEl = document.getElementById('runningCount');

        if (loadingEl) loadingEl.style.display = 'inline-flex';

        try {
            const response = await fetch('/api/running/active');
            const data = await response.json();

            // Update count badge
            if (countEl) countEl.textContent = `${data.length} Running`;

            this.dataCache.running = data;
            this.renderRunningTable(data);

        } catch (error) {
            console.error('Failed to load running queries:', error);
        } finally {
            if (loadingEl) loadingEl.style.display = 'none';
        }
    }

    renderRunningTable(data) {
        const tbody = document.querySelector('#runningTable tbody');
        const state = this.sortState.runningTable;
        const sortedData = this.sortData(data, state.col, state.dir);

        tbody.innerHTML = '';

        if (!sortedData || sortedData.length === 0) {
            tbody.innerHTML = '<tr><td colspan="10" class="empty-state">No queries currently running</td></tr>';
            return;
        }

        sortedData.forEach(query => {
            const row = document.createElement('tr');
            const statusClass = this.getStatusClass(query.status);
            row.innerHTML = `
                <td>${query.sessionId}</td>
                <td>${query.databaseName}</td>
                <td><span class="badge ${statusClass}">${query.status}</span></td>
                <td>${query.command}</td>
                <td class="number">${this.formatNumber(query.elapsedTimeMs)}</td>
                <td class="number">${this.formatNumber(query.cpuTimeMs)}</td>
                <td class="number">${this.formatNumber(query.logicalReads)}</td>
                <td>${query.waitType || '-'}</td>
                <td>${query.hostName}</td>
                <td class="query-text" title="${this.escapeHtml(query.queryText)}">${this.truncateText(query.queryText, 40)}</td>
            `;

            const queryCell = row.querySelector('.query-text');
            if (queryCell && query.queryText) {
                queryCell.addEventListener('click', () => {
                    this.showQueryModal(query.queryText);
                });
            }
            tbody.appendChild(row);
        });
    }

    getStatusClass(status) {
        switch (status?.toLowerCase()) {
            case 'running': return 'running';
            case 'suspended': return 'suspended';
            case 'sleeping': return 'sleeping';
            default: return 'info';
        }
    }

    handleSort(tableId, col) {
        const state = this.sortState[tableId] || { col: null, dir: 'desc' };

        // Toggle direction if same column
        if (state.col === col) {
            state.dir = state.dir === 'desc' ? 'asc' : 'desc';
        } else {
            state.col = col;
            state.dir = 'asc'; // Default to ascending for text, desc for numbers? 
            // Better defaulting: Default to desc for most metrics.
            if (['databaseName', 'queryText'].includes(col)) state.dir = 'asc';
            else state.dir = 'desc';
        }

        this.sortState[tableId] = state;
        this.updateSortIcons(tableId);

        // Re-render
        // Re-render
        switch (tableId) {
            case 'queriesTable': if (this.dataCache.queries) this.renderQueriesTable(this.dataCache.queries); break;
            case 'topCpuTable': if (this.dataCache.topCpu) this.renderTopCpuTable(this.dataCache.topCpu); break;
            case 'runningTable': if (this.dataCache.running) this.renderRunningTable(this.dataCache.running); break;
            case 'indexesTable': if (this.dataCache.indexes) this.renderIndexesTable(this.dataCache.indexes); break;
            case 'blockingTable': if (this.dataCache.blocking) this.renderBlockingTable(this.dataCache.blocking, false); break;
            case 'blockingFullTable': if (this.dataCache.blocking) this.renderBlockingTable(this.dataCache.blocking, true); break;
            case 'locksTable': if (this.dataCache.locks) this.renderLocksTable(this.dataCache.locks); break;
            case 'blockingHistoryTable': if (this.dataCache.blockingHistory) this.renderBlockingHistoryTable(this.dataCache.blockingHistory); break;
        }
    }

    updateSortIcons(tableId) {
        const table = document.getElementById(tableId);
        if (!table) return;

        const state = this.sortState[tableId];
        const headers = table.querySelectorAll('th.sortable');

        headers.forEach(th => {
            th.classList.remove('sort-asc', 'sort-desc');
            const invoke = th.getAttribute('onclick') || '';
            if (invoke.includes(`'${state.col}'`)) {
                th.classList.add(state.dir === 'asc' ? 'sort-asc' : 'sort-desc');
            }
        });
    }

    sortData(data, col, dir) {
        if (!col || !data) return data;

        return [...data].sort((a, b) => {
            let valA = a[col];
            let valB = b[col];

            // Handle nulls
            if (valA === null || valA === undefined) valA = '';
            if (valB === null || valB === undefined) valB = '';

            // Strings (case insensitive)
            if (typeof valA === 'string') valA = valA.toLowerCase();
            if (typeof valB === 'string') valB = valB.toLowerCase();

            if (valA < valB) return dir === 'asc' ? -1 : 1;
            if (valA > valB) return dir === 'asc' ? 1 : -1;
            return 0;
        });
    }

    async loadQueries(type) {
        const tbody = document.querySelector('#queriesTable tbody');

        // Setup default sort based on tab
        let defCol = 'avgCpuTimeMs';
        if (type === 'io') defCol = 'avgLogicalReads';
        else if (type === 'slowest') defCol = 'avgElapsedTimeMs';

        if (this.currentQueryTab !== type) {
            this.sortState.queriesTable = { col: defCol, dir: 'desc' };
            this.updateSortIcons('queriesTable');
        }

        // Only show loading if empty
        const hasData = tbody.querySelector('tr') && !tbody.querySelector('.loading-row') && !tbody.querySelector('.empty-state');
        if (!hasData) {
            tbody.innerHTML = '<tr><td colspan="9" class="loading-row"><span class="loading"></span> Loading...</td></tr>';
        }

        try {
            // All tabs now use historical data with time range
            const hours = document.getElementById('queryHistoryRange')?.value || 1;
            const sortBy = type === 'cpu' ? 'cpu' : type === 'io' ? 'io' : 'elapsed';
            const url = `/api/queries/history?hours=${hours}&sortBy=${sortBy}&_=${Date.now()}`;

            const response = await fetch(url);
            const data = await response.json();

            this.dataCache.queries = data;
            this.renderQueriesTable(data);
        } catch (error) {
            console.error('Failed to load queries:', error);
            if (!hasData) tbody.innerHTML = '<tr><td colspan="9" class="empty-state">Failed to load data</td></tr>';
        }
    }

    renderQueriesTable(data) {
        const tbody = document.querySelector('#queriesTable tbody');
        const state = this.sortState.queriesTable;

        const sortedData = this.sortData(data, state.col, state.dir);

        tbody.innerHTML = '';

        if (!sortedData || sortedData.length === 0) {
            tbody.innerHTML = '<tr><td colspan="9" class="empty-state">No query data available</td></tr>';
            return;
        }

        sortedData.forEach(query => {
            const row = document.createElement('tr');
            row.innerHTML = `
                <td>${query.databaseName || 'Unknown'}</td>
                <td class="query-text" title="${this.escapeHtml(query.queryText)}">${this.truncateText(query.queryText, 60)}</td>
                <td class="number">${this.formatNumber(query.executionCount)}</td>
                <td class="number">${this.formatNumber(Math.round(query.avgCpuTimeMs))}</td>
                <td class="number">${this.formatNumber(Math.round(query.totalCpuTimeMs))}</td>
                <td class="number">${this.formatNumber(query.avgLogicalReads)}</td>
                <td class="number">${this.formatNumber(query.avgLogicalWrites)}</td>
                <td class="number">${this.formatNumber(Math.round(query.avgElapsedTimeMs))}</td>
                <td>${this.formatDate(query.lastExecutionTime)}</td>
            `;
            row.querySelector('.query-text').addEventListener('click', () => {
                this.showQueryModal(query.queryText);
            });
            tbody.appendChild(row);
        });
    }

    async loadMissingIndexes() {
        const tbody = document.querySelector('#indexesTable tbody');

        // Show loading indicator
        tbody.innerHTML = '<tr><td colspan="8" class="loading-row"><span class="loading"></span> Loading...</td></tr>';

        try {
            const response = await fetch('/api/indexes/missing?top=50');
            const data = await response.json();

            this.dataCache.indexes = data;
            this.renderIndexesTable(data);

        } catch (error) {
            console.error('Failed to load missing indexes:', error);
            tbody.innerHTML = '<tr><td colspan="8" class="empty-state">Failed to load data</td></tr>';
        }
    }

    renderIndexesTable(data) {
        const tbody = document.querySelector('#indexesTable tbody');
        const state = this.sortState.indexesTable;
        const sortedData = this.sortData(data, state.col, state.dir);

        tbody.innerHTML = '';

        if (!sortedData || sortedData.length === 0) {
            tbody.innerHTML = '<tr><td colspan="8" class="empty-state">No missing index recommendations</td></tr>';
            return;
        }

        sortedData.forEach(index => {
            const row = document.createElement('tr');
            row.innerHTML = `
                <td>${index.databaseName}</td>
                <td>${index.schemaName}.${index.tableName}</td>
                <td class="number">${this.formatNumber(Math.round(index.improvementMeasure))}</td>
                <td class="number">${this.formatNumber(index.userSeeks)}</td>
                <td class="number">${index.avgUserImpact.toFixed(1)}%</td>
                <td>${index.equalityColumns || '-'}</td>
                <td>${index.inequalityColumns || '-'}</td>
                <td>
                    <button class="copy-code" onclick="navigator.clipboard.writeText('${this.escapeHtml(index.createIndexStatement)}')">Copy</button>
                </td>
            `;
            tbody.appendChild(row);
        });
    }

    async loadBlockingSessions(fullTable = false) {
        // Show loading indicators first
        if (!fullTable) {
            const dashboardTbody = document.querySelector('#blockingTable tbody');
            dashboardTbody.innerHTML = '<tr><td colspan="3" class="loading-row"><span class="loading"></span> Loading...</td></tr>';
        }
        if (fullTable || this.currentSection === 'blocking') {
            const fullTbody = document.querySelector('#blockingFullTable tbody');
            fullTbody.innerHTML = '<tr><td colspan="9" class="loading-row"><span class="loading"></span> Loading...</td></tr>';
        }

        try {
            const response = await fetch('/api/blocking/active');
            const data = await response.json();

            this.dataCache.blocking = data;

            // Update dashboard quick view
            if (!fullTable) this.renderBlockingTable(data, false);

            // Update full table if on blocking section
            if (fullTable || this.currentSection === 'blocking') {
                document.getElementById('blockingCount').textContent = `${data.length} Blocked`;
                this.renderBlockingTable(data, true);
            }

        } catch (error) {
            console.error('Failed to load blocking sessions:', error);
        }
    }

    renderBlockingTable(data, fullTable) {
        if (!fullTable) {
            const tbody = document.querySelector('#blockingTable tbody');
            // Dashboard widget: Apply sort, but typically just top N
            const state = this.sortState.blockingTable;
            // Default dashboard expectation: Most blocked/highest wait?
            // Existing logic slices 5. We will sort ALL then slice 5.
            const sortedData = this.sortData(data, state.col, state.dir);

            tbody.innerHTML = '';

            if (!sortedData || sortedData.length === 0) {
                tbody.innerHTML = '<tr><td colspan="3" class="empty-state">No active blocking</td></tr>';
            } else {
                sortedData.slice(0, 5).forEach(session => {
                    const row = document.createElement('tr');
                    if (session.isLeadBlocker) row.className = 'lead-blocker';
                    row.innerHTML = `
                        <td>${session.sessionId}</td>
                        <td>${session.blockingSessionId || '-'}</td>
                        <td class="number">${this.formatNumber(session.waitTimeMs)}</td>
                    `;
                    tbody.appendChild(row);
                });
            }
        } else {
            const tbody = document.querySelector('#blockingFullTable tbody');
            const state = this.sortState.blockingFullTable;
            const sortedData = this.sortData(data, state.col, state.dir);

            tbody.innerHTML = '';

            if (!sortedData || sortedData.length === 0) {
                tbody.innerHTML = '<tr><td colspan="9" class="empty-state">No active blocking sessions</td></tr>';
                return;
            }

            sortedData.forEach(session => {
                const row = document.createElement('tr');
                if (session.isLeadBlocker) row.className = 'lead-blocker';
                row.innerHTML = `
                    <td>${session.sessionId}</td>
                    <td>${session.blockingSessionId || '-'}</td>
                    <td><span class="badge ${session.status === 'running' ? 'success' : 'warning'}">${session.status}</span></td>
                    <td>${session.waitType || '-'}</td>
                    <td class="number">${this.formatNumber(session.waitTimeMs)}</td>
                    <td>${session.databaseName}</td>
                    <td>${session.hostName}</td>
                    <td>${session.loginName}</td>
                    <td class="query-text" title="${this.escapeHtml(session.queryText)}">${this.truncateText(session.queryText, 50)}</td>
                `;
                const queryCell = row.querySelector('.query-text');
                if (queryCell && session.queryText) {
                    queryCell.addEventListener('click', () => {
                        this.showQueryModal(session.queryText);
                    });
                }
                tbody.appendChild(row);
            });
        }
    }

    async loadLocks() {
        const tbody = document.querySelector('#locksTable tbody');

        // Show loading indicator
        tbody.innerHTML = '<tr><td colspan="9" class="loading-row"><span class="loading"></span> Loading...</td></tr>';

        try {
            const response = await fetch('/api/locks/current');
            const data = await response.json();

            this.dataCache.locks = data;
            this.renderLocksTable(data);

        } catch (error) {
            console.error('Failed to load locks:', error);
            tbody.innerHTML = '<tr><td colspan="9" class="empty-state">Failed to load data</td></tr>';
        }
    }

    renderLocksTable(data) {
        const tbody = document.querySelector('#locksTable tbody');
        const state = this.sortState.locksTable;
        const sortedData = this.sortData(data, state.col, state.dir);

        tbody.innerHTML = '';

        if (!sortedData || sortedData.length === 0) {
            tbody.innerHTML = '<tr><td colspan="9" class="empty-state">No active locks</td></tr>';
            return;
        }

        sortedData.forEach(lock => {
            const row = document.createElement('tr');
            row.innerHTML = `
                <td>${lock.sessionId}</td>
                <td>${lock.databaseName}</td>
                <td>${lock.objectName || '-'}</td>
                <td>${lock.resourceType}</td>
                <td><span class="badge ${this.getLockModeBadgeClass(lock.requestMode)}">${lock.requestMode}</span></td>
                <td>${lock.requestStatus}</td>
                <td class="number">${lock.requestCount}</td>
                <td>${lock.hostName}</td>
                <td>${lock.loginName}</td>
            `;
            tbody.appendChild(row);
        });
    }

    getLockModeBadgeClass(mode) {
        const exclusiveModes = ['X', 'IX', 'SIX', 'UIX'];
        const sharedModes = ['S', 'IS'];

        if (exclusiveModes.includes(mode)) return 'danger';
        if (sharedModes.includes(mode)) return 'info';
        return 'warning';
    }

    showQueryModal(queryText) {
        document.getElementById('queryDetail').textContent = queryText;
        document.getElementById('queryModal').classList.add('active');
    }

    closeModal() {
        document.querySelectorAll('.modal').forEach(m => m.classList.remove('active'));
    }

    async showBlockingHistory() {
        const modal = document.getElementById('blockingHistoryModal');
        modal.classList.add('active');
        const tbody = document.querySelector('#blockingHistoryTable tbody');
        tbody.innerHTML = '<tr><td colspan="4" class="loading-row"><span class="loading"></span> Loading history...</td></tr>';

        try {
            const response = await fetch(`/api/metrics/blocking-history?rangeSeconds=172800&_=${Date.now()}`); // 2 days
            const data = await response.json();

            // Process data
            const history = [];
            data.forEach(point => {
                if (point.blockedQueries && point.blockedQueries.length > 0) {
                    point.blockedQueries.forEach(bq => {
                        // Include only blocked sessions (those waiting on someone)
                        if (bq.blockingSessionId && bq.blockingSessionId !== 0) {
                            const blocker = point.blockedQueries.find(b => b.sessionId === bq.blockingSessionId);
                            const blockerText = blocker ? `[${blocker.sessionId}] ${blocker.queryTextPreview}` : `[${bq.blockingSessionId}] (Unknown)`;

                            history.push({
                                time: point.timestamp,
                                blocked: `[${bq.sessionId}] ${bq.queryTextPreview}`,
                                blocker: blockerText,
                                wait: bq.waitTimeMs
                            });
                        }
                    });
                }
            });

            this.dataCache.blockingHistory = history;
            this.renderBlockingHistoryTable(history);

        } catch (error) {
            console.error(error);
            tbody.innerHTML = '<tr><td colspan="4" class="empty-state">Failed to load history</td></tr>';
        }
    }

    renderBlockingHistoryTable(data) {
        const tbody = document.querySelector('#blockingHistoryTable tbody');
        const state = this.sortState.blockingHistoryTable;
        const sortedData = this.sortData(data, state.col, state.dir);

        tbody.innerHTML = '';
        if (!sortedData || sortedData.length === 0) {
            tbody.innerHTML = '<tr><td colspan="4" class="empty-state">No blocking events recorded in history</td></tr>';
            return;
        }

        // Limit to top 500 events to prevent browser lag if huge history
        const displayHistory = sortedData.slice(0, 500);

        displayHistory.forEach(item => {
            const row = document.createElement('tr');
            row.innerHTML = `
                <td>${this.formatDate(item.time)}</td>
                <td title="${this.escapeHtml(item.blocked)}">${this.truncateText(item.blocked, 60)}</td>
                <td title="${this.escapeHtml(item.blocker)}">${this.truncateText(item.blocker, 60)}</td>
                <td class="number">${this.formatNumber(item.wait)}</td>
            `;
            tbody.appendChild(row);
        });
    }

    // Utility functions
    formatNumber(num) {
        if (num === null || num === undefined) return '-';
        return new Intl.NumberFormat().format(num);
    }

    formatDate(dateStr) {
        if (!dateStr) return '-';
        const date = new Date(dateStr);
        return date.toLocaleString();
    }

    formatUptime(uptime) {
        if (!uptime) return '-';

        // Parse .NET TimeSpan format (e.g., "5.02:30:45.123456")
        const match = uptime.match(/^(\d+)?\.?(\d{2}):(\d{2}):(\d{2})/);
        if (match) {
            const days = match[1] ? parseInt(match[1]) : 0;
            const hours = parseInt(match[2]);
            const minutes = parseInt(match[3]);

            if (days > 0) {
                return `${days}d ${hours}h ${minutes}m`;
            }
            return `${hours}h ${minutes}m`;
        }
        return uptime;
    }

    truncateText(text, maxLen) {
        if (!text) return '-';
        text = text.replace(/\s+/g, ' ').trim();
        if (text.length <= maxLen) return text;
        return text.substring(0, maxLen) + '...';
    }

    escapeHtml(text) {
        if (!text) return '';
        return text.replace(/[&<>"']/g, char => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        }[char]));
    }
}

// Initialize app when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    window.app = window.sqlMonitor = new SqlMonitorApp();
});

