/**
 * Multi-Connection Manager Module
 * Handles multiple SQL Server connections with secure storage.
 * 
 * Features:
 * - Add/remove/test connections
 * - Connection status monitoring
 * - Enforces setup before viewing statistics
 */

const MultiConnectionManager = {
    connections: [],
    maxConnections: 5,
    isInitialized: false,

    /**
     * Initialize the connection manager
     */
    async init() {
        this.bindEvents();

        // Only load connections if user is authenticated
        if (this.isUserAuthenticated()) {
            await this.loadConnections();
        }

        this.checkSetupRequired();
        this.isInitialized = true;
    },

    /**
     * Check if user is authenticated
     */
    isUserAuthenticated() {
        // Check if auth manager exists and user is authenticated
        return window.authManager && window.authManager.isAuthenticated;
    },

    /**
     * Bind event handlers for setup UI
     */
    bindEvents() {
        // Add connection button
        document.getElementById('addConnectionBtn')?.addEventListener('click', () => {
            this.showAddConnectionModal();
        });

        // Modal controls
        document.getElementById('closeAddConnectionModal')?.addEventListener('click', () => {
            this.hideAddConnectionModal();
        });

        document.getElementById('cancelAddConnection')?.addEventListener('click', () => {
            this.hideAddConnectionModal();
        });

        // Save new connection
        document.getElementById('saveNewConnection')?.addEventListener('click', () => {
            this.saveNewConnection();
        });

        // Windows Auth toggle in modal
        document.getElementById('newWindowsAuthCheck')?.addEventListener('change', (e) => {
            const sqlAuthFields = document.getElementById('newSqlAuthFields');
            if (e.target.checked) {
                sqlAuthFields.classList.add('hidden');
            } else {
                sqlAuthFields.classList.remove('hidden');
            }
        });

        // Close modal on overlay click
        document.querySelector('#addConnectionModal .modal-overlay')?.addEventListener('click', () => {
            this.hideAddConnectionModal();
        });

        // Go to setup button (from setup required overlay)
        document.getElementById('goToSetupBtn')?.addEventListener('click', () => {
            if (window.app) {
                window.app.navigateTo('setup');
            }
        });
    },

    /**
     * Load connections after user authentication
     */
    async loadConnectionsAfterAuth() {
        if (!this.isUserAuthenticated()) {
            console.warn('Cannot load connections: User not authenticated');
            return;
        }
        await this.loadConnections();
    },

    /**
     * Load all connections from API with retry logic
     */
    async loadConnections(retryCount = 0) {
        // Check authentication first
        if (!this.isUserAuthenticated()) {
            console.log('Skipping connection loading: User not authenticated');
            return { connections: [], maxConnections: 5 };
        }

        const maxRetries = 3;

        try {
            const response = await fetch('/api/connections');

            if (!response.ok) {
                // Handle authentication errors
                if (response.status === 401) {
                    console.log('Connection loading failed: Not authenticated');
                    return { connections: [], maxConnections: 5 };
                }
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();

            this.connections = data.connections || [];
            this.maxConnections = data.maxConnections || 5;

            this.updateConnectionsUI();
            this.updateLimitBadge();
            this.checkSetupRequired();

            return data;
        } catch (error) {
            console.error('Failed to load connections:', error);

            // Retry on network errors (but not auth errors)
            if (retryCount < maxRetries && (error.message.includes('fetch') || error.message.includes('network'))) {
                const delay = Math.pow(2, retryCount) * 1000; // Exponential backoff
                await new Promise(resolve => setTimeout(resolve, delay));
                return this.loadConnections(retryCount + 1);
            }

            // Show user-facing error after retries exhausted (but not for auth errors)
            if (!error.message.includes('401')) {
                this.showError('Unable to load connections. Please check your network and try again.');
            }
            return { connections: [], maxConnections: 5 };
        }
    },

    /**
     * Check if setup is required (no connections configured)
     */
    checkSetupRequired() {
        const hasConnections = this.connections.length > 0;
        const overlay = document.getElementById('setupRequiredOverlay');

        if (!hasConnections) {
            // Show setup required overlay if not already present
            if (!overlay) {
                this.showSetupRequiredOverlay();
            }
            return true;
        } else {
            // Hide overlay if connections exist
            if (overlay) {
                overlay.remove();
            }
            return false;
        }
    },

    /**
     * Show overlay requiring user to set up at least one connection
     */
    showSetupRequiredOverlay() {
        // Don't show if already on setup page
        if (window.app?.currentSection === 'setup') {
            return;
        }

        const overlay = document.createElement('div');
        overlay.id = 'setupRequiredOverlay';
        overlay.className = 'setup-required-overlay';
        overlay.innerHTML = `
            <div class="setup-required-content">
                <svg viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 3C7.58 3 4 4.79 4 7s3.58 4 8 4 8-1.79 8-4-3.58-4-8-4zM4 9v3c0 2.21 3.58 4 8 4s8-1.79 8-4V9c0 2.21-3.58 4-8 4s-8-1.79-8-4zm0 5v3c0 2.21 3.58 4 8 4s8-1.79 8-4v-3c0 2.21-3.58 4-8 4s-8-1.79-8-4z"/>
                </svg>
                <h2>Setup Required</h2>
                <p>You need to configure at least one SQL Server connection before you can view monitoring statistics.</p>
                <button class="btn btn-primary" id="goToSetupBtn">
                    <svg viewBox="0 0 24 24" fill="currentColor" style="width: 18px; height: 18px;">
                        <path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z"/>
                    </svg>
                    Add Connection
                </button>
            </div>
        `;

        document.body.appendChild(overlay);

        // Bind the button
        document.getElementById('goToSetupBtn')?.addEventListener('click', () => {
            if (window.app) {
                window.app.navigateTo('setup');
            }
        });
    },

    /**
     * Update the connections grid UI
     */
    updateConnectionsUI() {
        const grid = document.getElementById('connectionsGrid');
        const noConnectionsMsg = document.getElementById('noConnectionsMessage');

        if (!grid) return;

        if (this.connections.length === 0) {
            grid.innerHTML = '';
            if (noConnectionsMsg) {
                noConnectionsMsg.style.display = 'block';
            }
            return;
        }

        if (noConnectionsMsg) {
            noConnectionsMsg.style.display = 'none';
        }

        grid.innerHTML = this.connections.map(conn => this.renderConnectionCard(conn)).join('');

        // Bind action buttons
        this.connections.forEach(conn => {
            // Click on card itself (not buttons) to select connection
            const card = document.querySelector(`.connection-card[data-connection-id="${conn.id}"]`);
            if (card && conn.isEnabled) {
                card.style.cursor = 'pointer';
                card.addEventListener('click', (e) => {
                    // Don't trigger if clicking on a button
                    if (e.target.tagName === 'BUTTON' || e.target.closest('button')) {
                        return;
                    }
                    if (window.app) {
                        window.app.setActiveConnection(conn.id);
                    }
                });
            }

            document.getElementById(`connect-${conn.id}`)?.addEventListener('click', () => {
                if (window.app) {
                    window.app.setActiveConnection(conn.id);
                }
                // If not connected, force a test to reconnect immediately
                if (conn.status !== 1) {
                    this.testConnection(conn.id);
                }
            });

            // Disconnect button - stops data collection for this connection
            document.getElementById(`disconnect-${conn.id}`)?.addEventListener('click', () => {
                this.disconnectConnection(conn.id);
            });

            document.getElementById(`test-${conn.id}`)?.addEventListener('click', () => {
                this.testConnection(conn.id);
            });

            document.getElementById(`toggle-${conn.id}`)?.addEventListener('click', () => {
                this.toggleConnection(conn.id, !conn.isEnabled);
            });

            document.getElementById(`remove-${conn.id}`)?.addEventListener('click', () => {
                this.removeConnection(conn.id, conn.name);
            });
        });
    },

    /**
     * Render a single connection card
     */
    renderConnectionCard(conn) {
        const statusClass = this.getStatusClass(conn.status);
        const statusLabel = this.getStatusLabel(conn.status);
        const cardClass = conn.isEnabled ? statusClass : 'disabled';
        const isConnected = conn.status === 1 && conn.isEnabled;

        const isActive = window.app && window.app.activeConnectionId === conn.id;
        const activeClass = isActive ? 'active-connection' : '';

        // Button logic:
        // - When connected: Show "Disconnect" button (to stop data collection)
        // - When not connected: Show "Connect" button
        // - When active: Show "Active" badge instead
        let connectBtn;
        if (isActive) {
            connectBtn = `<button class="btn btn-success" disabled style="opacity: 1; cursor: default;">Active</button>`;
        } else if (isConnected) {
            connectBtn = `<button class="btn btn-warning" id="disconnect-${conn.id}">Disconnect</button>`;
        } else {
            connectBtn = `<button class="btn btn-primary" id="connect-${conn.id}" ${!conn.isEnabled ? 'disabled' : ''}>Connect</button>`;
        }

        // Test button: disabled when already connected
        const testBtnDisabled = isConnected ? 'disabled' : '';
        const testBtnStyle = isConnected ? 'opacity: 0.5; cursor: not-allowed;' : '';

        return `
            <div class="connection-card ${cardClass} ${activeClass}" data-connection-id="${conn.id}">
                <div class="connection-card-header">
                    <div>
                        <div class="connection-name">${this.escapeHtml(conn.name)}</div>
                        <div class="connection-server">${this.escapeHtml(conn.server)}/${this.escapeHtml(conn.database)}</div>
                    </div>
                    <span class="connection-status-badge ${statusClass}">
                        <span class="status-dot ${statusClass}"></span>
                        ${statusLabel}
                    </span>
                </div>
                
                <div class="connection-details">
                    <div class="connection-detail">
                        <span class="connection-detail-label">Auth</span>
                        <span class="connection-detail-value">${conn.useWindowsAuth ? 'Windows' : conn.username || 'SQL'}</span>
                    </div>
                    <div class="connection-detail">
                        <span class="connection-detail-label">Last Connected</span>
                        <span class="connection-detail-value">${conn.lastSuccessfulConnection ? this.formatDate(conn.lastSuccessfulConnection) : 'Never'}</span>
                    </div>
                </div>
                
                ${conn.lastError ? `<div class="connection-error">${this.escapeHtml(conn.lastError)}</div>` : ''}
                
                <div class="connection-actions">
                    ${connectBtn}
                    <button class="btn btn-outline" id="test-${conn.id}" ${testBtnDisabled} style="${testBtnStyle}">
                        Test
                    </button>
                    <button class="btn btn-outline" id="toggle-${conn.id}">
                        ${conn.isEnabled ? 'Disable' : 'Enable'}
                    </button>
                    <button class="btn btn-danger" id="remove-${conn.id}">
                        Remove
                    </button>
                </div>
            </div>
        `;
    },

    /**
     * Update the connection limit badge
     */
    updateLimitBadge() {
        const badge = document.getElementById('connectionLimitBadge');
        if (badge) {
            badge.textContent = `${this.connections.length} / ${this.maxConnections} connections`;
        }

        // Disable add button if at limit
        const addBtn = document.getElementById('addConnectionBtn');
        if (addBtn) {
            addBtn.disabled = this.connections.length >= this.maxConnections;
        }
    },

    /**
     * Show the add connection modal
     */
    showAddConnectionModal() {
        const modal = document.getElementById('addConnectionModal');
        if (modal) {
            // Reset form
            document.getElementById('addConnectionForm')?.reset();
            document.getElementById('addConnectionResult').style.display = 'none';
            document.getElementById('newSqlAuthFields').classList.remove('hidden');

            modal.classList.add('active');
        }
    },

    /**
     * Hide the add connection modal
     */
    hideAddConnectionModal() {
        const modal = document.getElementById('addConnectionModal');
        if (modal) {
            modal.classList.remove('active');
        }
    },

    /**
     * Save a new connection
     */
    async saveNewConnection() {
        const btn = document.getElementById('saveNewConnection');
        const btnText = btn.querySelector('.btn-text');
        const spinner = btn.querySelector('.loading');
        const resultDiv = document.getElementById('addConnectionResult');

        // Gather form data
        const data = {
            name: document.getElementById('newConnectionName').value.trim(),
            server: document.getElementById('newServerInput').value.trim(),
            database: document.getElementById('newDatabaseInput').value.trim() || 'master',
            useWindowsAuth: document.getElementById('newWindowsAuthCheck').checked,
            username: document.getElementById('newUsernameInput').value.trim(),
            password: document.getElementById('newPasswordInput').value,
            trustCertificate: document.getElementById('newTrustCertCheck').checked,
            timeout: parseInt(document.getElementById('newTimeoutInput').value) || 30
        };

        // Validate
        if (!data.server) {
            this.showModalResult(false, 'Server name is required.');
            return;
        }

        if (!data.useWindowsAuth && !data.username) {
            this.showModalResult(false, 'Username is required for SQL Server authentication.');
            return;
        }

        // Show loading
        btnText.textContent = 'Testing...';
        spinner.style.display = 'inline-block';
        btn.disabled = true;

        try {
            const response = await fetch('/api/connections', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });

            const result = await response.json();

            if (result.success) {
                this.showModalResult(true, 'Connection added successfully!');

                // Reload connections and close modal
                await this.loadConnections();

                setTimeout(() => {
                    this.hideAddConnectionModal();
                }, 1500);
            } else {
                this.showModalResult(false, result.message);
            }
        } catch (error) {
            this.showModalResult(false, 'Failed to add connection: ' + error.message);
        } finally {
            btnText.textContent = 'Add & Test Connection';
            spinner.style.display = 'none';
            btn.disabled = false;
        }
    },

    /**
     * Show result message in modal
     */
    showModalResult(success, message) {
        const resultDiv = document.getElementById('addConnectionResult');
        resultDiv.style.display = 'block';
        resultDiv.className = 'connection-result ' + (success ? 'success' : 'error');
        resultDiv.textContent = message;
    },

    /**
     * Test a connection
     */
    async testConnection(connectionId) {
        const card = document.querySelector(`[data-connection-id="${connectionId}"]`);
        const btn = document.getElementById(`test-${connectionId}`);

        if (btn) {
            btn.textContent = 'Testing...';
            btn.disabled = true;
        }

        try {
            const response = await fetch(`/api/connections/${connectionId}/test`, {
                method: 'POST'
            });

            const result = await response.json();

            // Reload to get updated status
            await this.loadConnections();

        } catch (error) {
            console.error('Failed to test connection:', error);
        } finally {
            if (btn) {
                btn.textContent = 'Test';
                btn.disabled = false;
            }
        }
    },

    /**
     * Toggle connection enabled/disabled
     */
    async toggleConnection(connectionId, enabled) {
        try {
            const response = await fetch(`/api/connections/${connectionId}/enable`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ enabled })
            });

            await this.loadConnections();
        } catch (error) {
            console.error('Failed to toggle connection:', error);
        }
    },

    /**
     * Disconnect a connection - stops data collection
     */
    async disconnectConnection(connectionId) {
        const btn = document.getElementById(`disconnect-${connectionId}`);

        if (btn) {
            btn.textContent = 'Disconnecting...';
            btn.disabled = true;
        }

        try {
            const response = await fetch(`/api/connections/${connectionId}/disconnect`, {
                method: 'POST'
            });

            if (response.ok) {
                this.showSuccess('Connection disconnected');
            }

            // Reload to get updated status
            await this.loadConnections();
        } catch (error) {
            console.error('Failed to disconnect:', error);
            this.showError('Failed to disconnect');
        }
    },

    /**
     * Remove a connection
     */
    async removeConnection(connectionId, connectionName) {
        if (!confirm(`Are you sure you want to remove the connection "${connectionName}"?`)) {
            return;
        }

        try {
            const response = await fetch(`/api/connections/${connectionId}`, {
                method: 'DELETE'
            });

            await this.loadConnections();
        } catch (error) {
            console.error('Failed to remove connection:', error);
        }
    },

    /**
     * Get CSS class for connection status
     */
    getStatusClass(status) {
        switch (status) {
            case 1: return 'connected';
            case 2: return 'disconnected';
            case 3: return 'error';
            case 4: return 'testing';
            default: return 'unknown';
        }
    },

    /**
     * Get label for connection status
     */
    getStatusLabel(status) {
        switch (status) {
            case 1: return 'Connected';
            case 2: return 'Disconnected';
            case 3: return 'Error';
            case 4: return 'Testing';
            default: return 'Unknown';
        }
    },

    /**
     * Format date for display
     */
    formatDate(dateString) {
        if (!dateString) return 'Never';
        const date = new Date(dateString);
        return date.toLocaleString();
    },

    /**
     * Escape HTML to prevent XSS
     */
    escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    },

    /**
     * Get the count of healthy connections
     */
    getHealthyConnectionCount() {
        return this.connections.filter(c => c.status === 1 && c.isEnabled).length;
    },

    /**
     * Check if any connections are configured
     */
    hasConnections() {
        return this.connections.length > 0;
    },

    /**
     * Show a toast notification to the user
     */
    showToast(message, type = 'info', duration = 4000) {
        // Remove existing toast if any
        const existing = document.querySelector('.toast-notification');
        if (existing) existing.remove();

        const toast = document.createElement('div');
        toast.className = `toast-notification toast-${type}`;
        toast.innerHTML = `
            <span class="toast-message">${this.escapeHtml(message)}</span>
            <button class="toast-close" onclick="this.parentElement.remove()">&times;</button>
        `;

        // Add styles if not already added
        if (!document.getElementById('toast-styles')) {
            const style = document.createElement('style');
            style.id = 'toast-styles';
            style.textContent = `
                .toast-notification {
                    position: fixed;
                    bottom: 20px;
                    right: 20px;
                    padding: 12px 20px;
                    border-radius: 8px;
                    color: white;
                    font-size: 14px;
                    display: flex;
                    align-items: center;
                    gap: 12px;
                    box-shadow: 0 4px 12px rgba(0,0,0,0.3);
                    z-index: 10000;
                    animation: slideIn 0.3s ease;
                }
                .toast-info { background: linear-gradient(135deg, #3b82f6, #1d4ed8); }
                .toast-success { background: linear-gradient(135deg, #22c55e, #16a34a); }
                .toast-error { background: linear-gradient(135deg, #ef4444, #dc2626); }
                .toast-warning { background: linear-gradient(135deg, #f59e0b, #d97706); }
                .toast-close {
                    background: none;
                    border: none;
                    color: white;
                    font-size: 18px;
                    cursor: pointer;
                    opacity: 0.7;
                }
                .toast-close:hover { opacity: 1; }
                @keyframes slideIn {
                    from { transform: translateX(100%); opacity: 0; }
                    to { transform: translateX(0); opacity: 1; }
                }
            `;
            document.head.appendChild(style);
        }

        document.body.appendChild(toast);

        // Auto-remove after duration
        setTimeout(() => {
            if (toast.parentNode) {
                toast.style.animation = 'slideIn 0.3s ease reverse';
                setTimeout(() => toast.remove(), 300);
            }
        }, duration);
    },

    /**
     * Show error notification
     */
    showError(message) {
        this.showToast(message, 'error', 5000);
    },

    /**
     * Show success notification
     */
    showSuccess(message) {
        this.showToast(message, 'success', 3000);
    }
};

// Export for use in app.js
window.MultiConnectionManager = MultiConnectionManager;
