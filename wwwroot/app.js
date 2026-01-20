/**
 * SQL Server Monitoring Dashboard - JavaScript Application
 * Uses modular architecture with consolidated polling.
 */

class SqlMonitorApp {
    constructor() {
        this.currentSection = 'setup';
        this.currentQueryTab = 'cpu';

        this.dataCache = {};
        this.activeConnectionId = null;
        this.sortState = {};

        this.init();
    }

    async init() {
        this.authManager = new AuthManager(window.apiClient);
        window.authManager = this.authManager;
        await this.authManager.init();

        if (window.MultiConnectionManager) {
            await window.MultiConnectionManager.init();
        }

        this.bindEvents();

        this.hideConnectionMenus();

        this.restoreState();
    }

    async restoreState() {
        if (!this.authManager?.isAuthenticated) {
            this.navigateTo('setup');
            return;
        }

        await this.loadActiveConnection();

        const lastSection = sessionStorage.getItem('lastSection');
        const lastQueryTab = sessionStorage.getItem('lastQueryTab');

        if (lastQueryTab) {
            this.currentQueryTab = lastQueryTab;
            document.querySelectorAll('.tab-btn').forEach(btn => {
                btn.classList.toggle('active', btn.dataset.tab === lastQueryTab);
            });
        }

        if (this.activeConnectionId) {
            this.showConnectionMenus();

            if (lastSection && lastSection !== 'setup') {
                this.navigateTo(lastSection);
                this.loadSectionData(lastSection);
            } else {
                this.navigateTo('setup');
            }
        } else {
            this.navigateTo('setup');
        }
    }

    /**
     * Load the user's active connection ID from the server.
     * Called after authentication is established and when user logs in.
     * Note: This only loads the ID, it does NOT show menus - user must click Connect.
     */
    async loadActiveConnection() {
        if (!this.authManager?.isAuthenticated) {
            return;
        }

        try {
            const response = await fetch('/api/connections/active');
            if (response.ok) {
                const data = await response.json();
                this.activeConnectionId = data.activeConnectionId;
            }
        } catch (e) {
            console.error("Failed to load active connection", e);
        }
    }

    async setActiveConnection(connectionId) {
        this.activeConnectionId = connectionId;

        try {
            await fetch('/api/connections/active', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ connectionId: connectionId })
            });
        } catch (e) {
            console.error("Failed to save active connection preference", e);
        }

        this.showConnectionMenus();

        this.navigateTo('dashboard');
        this.loadSectionData('dashboard');
    }

    showConnectionMenus() {
        document.querySelectorAll('.nav-item.requires-connection').forEach(item => {
            item.style.display = '';
        });
    }

    hideConnectionMenus() {
        document.querySelectorAll('.nav-item.requires-connection').forEach(item => {
            item.style.display = 'none';
        });
    }



    clearActiveConnection() {
        this.activeConnectionId = null;

        this.hideConnectionMenus();

        this.navigateTo('setup');
    }

    async fetchWithAuth(url, options = {}) {
        if (!this.activeConnectionId) {
            throw new Error("No active connection selected");
        }

        const headers = options.headers || {};
        headers['X-Connection-Id'] = this.activeConnectionId;
        options.headers = headers;

        return fetch(url, options);
    }

    bindEvents() {
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const section = item.dataset.section;
                this.navigateTo(section);
            });
        });

        document.getElementById('changeConnectionBtn')?.addEventListener('click', () => {
            this.navigateTo('setup');
        });

        document.querySelectorAll('[data-nav]').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                this.navigateTo(link.dataset.nav);
            });
        });

    }

    navigateTo(section) {
        window.scrollTo(0, 0);
        this.currentSection = section;

        sessionStorage.setItem('lastSection', section);

        if (section === 'setup') {
            this.activeConnectionId = null;
            this.hideConnectionMenus();
        }

        document.querySelectorAll('.content-section').forEach(sec => {
            sec.classList.toggle('active', sec.id === section);
        });

        const titles = {
            'dashboard': ['Dashboard', 'SQL Server Performance Analytics (Grafana)'],
            'setup': ['Connection Setup', 'Manage SQL Server connections']
        };

        if (titles[section]) {
            document.getElementById('pageTitle').textContent = titles[section][0];
            document.getElementById('pageSubtitle').textContent = titles[section][1];
        }

        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.section === section);
        });

        this.loadSectionData(section);
    }

    loadSectionData(section) {
        switch (section) {
            case 'dashboard':
                this.loadGrafanaDashboard();
                break;
            case 'setup':
                if (window.MultiConnectionManager) {
                    window.MultiConnectionManager.loadConnections();
                }
                break;
        }
    }

    switchQueryTab(tab) {
        this.currentQueryTab = tab;
        sessionStorage.setItem('lastQueryTab', tab);

        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.tab === tab);
        });

        this.loadQueries(tab);
    }

    formatDateTimeLocal(date) {
        const pad = n => n.toString().padStart(2, '0');
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }

    async loadGrafanaDashboard() {
        const container = document.getElementById('grafana');
        const frame = document.getElementById('grafanaFrame');
        const loading = document.getElementById('grafanaLoading');
        const error = document.getElementById('grafanaError');

        if (frame.src && frame.src !== 'about:blank') return;

        loading.style.display = 'flex';
        error.style.display = 'none';
        frame.classList.remove('loaded');

        try {
            const response = await this.fetchWithAuth('/api/grafana/dashboard');

            if (!response.ok) {
                throw new Error('Failed to get dashboard URL');
            }

            const data = await response.json();

            if (data.embedUrl) {
                frame.src = data.embedUrl;

                frame.onload = () => {
                    loading.style.display = 'none';
                    frame.classList.add('loaded');
                };
            } else {
                throw new Error('No embed URL returned');
            }
        } catch (e) {
            console.error('Grafana load error:', e);
            loading.style.display = 'none';
            error.style.display = 'flex';
        }
    }

    async loadConnectionSettings() {
        const savedSettings = sessionStorage.getItem('sqlConnectionSettings');

        if (savedSettings) {
            try {
                const data = JSON.parse(savedSettings);
                this.populateConnectionForm(data);

                const autoConnect = sessionStorage.getItem('sqlAutoConnect');
                if (autoConnect === 'true') {
                    this.saveConnection();
                }
                return;
            } catch (e) {

            }
        }

        try {
            const response = await fetch('/api/settings/connection');
            const data = await response.json();

            if (data.configured) {
                this.populateConnectionForm(data);
            }
        } catch (error) {

        }
    }

    populateConnectionForm(data) {
        document.getElementById('serverInput').value = data.server || '';
        document.getElementById('databaseInput').value = data.database || 'master';
        document.getElementById('windowsAuthCheck').checked = data.useWindowsAuth || false;
        document.getElementById('usernameInput').value = data.username || '';
        document.getElementById('trustCertCheck').checked = data.trustCertificate !== false;
        document.getElementById('timeoutInput').value = data.timeout || 30;

        const sqlAuthFields = document.getElementById('sqlAuthFields');
        if (data.useWindowsAuth) {
            sqlAuthFields.classList.add('hidden');
        } else {
            sqlAuthFields.classList.remove('hidden');
        }
    }

    saveToSessionStorage() {
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

        document.getElementById('serverInput').value = '';
        document.getElementById('databaseInput').value = 'master';
        document.getElementById('windowsAuthCheck').checked = false;
        document.getElementById('usernameInput').value = '';
        document.getElementById('passwordInput').value = '';
        document.getElementById('trustCertCheck').checked = true;
        document.getElementById('timeoutInput').value = '30';
        document.getElementById('sqlAuthFields').classList.remove('hidden');

        fetch('/api/settings/connection', { method: 'DELETE' }).catch(() => { });

        this.showConnectionResult(true, 'Connection settings cleared');
    }

    closeModal() {
        document.querySelectorAll('.modal').forEach(m => m.classList.remove('active'));
    }

    showConfirm(title, message, callback) {
        const modal = document.getElementById('confirmationModal');
        if (!modal) return;

        document.getElementById('confirmTitle').textContent = title;
        document.getElementById('confirmMessage').innerText = message; // Use innerText to preserve structure if needed

        const btnConfirm = document.getElementById('btnConfirmAction');
        const newBtn = btnConfirm.cloneNode(true);
        btnConfirm.parentNode.replaceChild(newBtn, btnConfirm);

        newBtn.addEventListener('click', () => {
            this.closeModal();
            if (callback) callback();
        });

        modal.classList.add('active');
    }

}

document.addEventListener('DOMContentLoaded', () => {
    window.app = window.sqlMonitor = new SqlMonitorApp();
});
