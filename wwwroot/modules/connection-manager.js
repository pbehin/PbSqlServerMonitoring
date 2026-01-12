/**
 * Connection Manager Module
 * Handles SQL Server connection settings and storage
 * 
 * Security:
 * - Never stores password in session storage
 * - Clears password from memory after form submission
 * - Provides XSS protection through escapeHtml
 */
class ConnectionManager {
    constructor() {
        this.sessionStorageKey = 'sqlConnectionSettings';
        this.autoConnectKey = 'sqlAutoConnect';
    }

    /**
     * Get form data for connection
     * Note: Password is included but should be cleared after use
     */
    getFormData() {
        return {
            server: document.getElementById('serverInput')?.value.trim() || '',
            database: document.getElementById('databaseInput')?.value.trim() || 'master',
            useWindowsAuth: document.getElementById('windowsAuthCheck')?.checked || false,
            username: document.getElementById('usernameInput')?.value.trim() || '',
            password: document.getElementById('passwordInput')?.value || '',
            trustCertificate: document.getElementById('trustCertCheck')?.checked,
            timeout: parseInt(document.getElementById('timeoutInput')?.value) || 30
        };
    }

    /**
     * Clear the password field after form submission
     * Security: Prevents password from lingering in DOM
     */
    clearPasswordField() {
        const passwordInput = document.getElementById('passwordInput');
        if (passwordInput) {
            passwordInput.value = '';
        }
    }

    /**
     * Validate connection data
     */
    validate(data) {
        const errors = [];

        if (!data.server) {
            errors.push('Server name is required');
        }

        if (!data.useWindowsAuth) {
            if (!data.username) {
                errors.push('Username is required for SQL Authentication');
            }
            if (!data.password) {
                errors.push('Password is required for SQL Authentication');
            }
        }

        return {
            isValid: errors.length === 0,
            errors
        };
    }

    /**
     * Populate form with connection data
     */
    populateForm(data) {
        const serverInput = document.getElementById('serverInput');
        const databaseInput = document.getElementById('databaseInput');
        const windowsAuthCheck = document.getElementById('windowsAuthCheck');
        const usernameInput = document.getElementById('usernameInput');
        const trustCertCheck = document.getElementById('trustCertCheck');
        const timeoutInput = document.getElementById('timeoutInput');
        const sqlAuthFields = document.getElementById('sqlAuthFields');

        if (serverInput) serverInput.value = data.server || '';
        if (databaseInput) databaseInput.value = data.database || 'master';
        if (windowsAuthCheck) windowsAuthCheck.checked = data.useWindowsAuth || false;
        if (usernameInput) usernameInput.value = data.username || '';
        // Note: trustCertificate can be null (use server default)
        if (trustCertCheck) trustCertCheck.checked = data.trustCertificate !== false;
        if (timeoutInput) timeoutInput.value = data.timeout || 30;

        // Toggle SQL auth fields
        if (sqlAuthFields) {
            if (data.useWindowsAuth) {
                sqlAuthFields.classList.add('hidden');
            } else {
                sqlAuthFields.classList.remove('hidden');
            }
        }
    }

    /**
     * Clear the connection form
     */
    clearForm() {
        this.populateForm({
            server: '',
            database: 'master',
            useWindowsAuth: false,
            username: '',
            trustCertificate: null, // Use server default
            timeout: 30
        });

        this.clearPasswordField();

        const sqlAuthFields = document.getElementById('sqlAuthFields');
        if (sqlAuthFields) sqlAuthFields.classList.remove('hidden');
    }

    /**
     * Save settings to session storage (excludes password)
     * Security: Password is NEVER stored
     */
    saveToSession(data) {
        const sessionData = { ...data };
        delete sessionData.password; // Never store password

        sessionStorage.setItem(this.sessionStorageKey, JSON.stringify(sessionData));
        sessionStorage.setItem(this.autoConnectKey, 'true');
    }

    /**
     * Load settings from session storage
     */
    loadFromSession() {
        try {
            const saved = sessionStorage.getItem(this.sessionStorageKey);
            if (saved) {
                return JSON.parse(saved);
            }
        } catch (e) {
            console.log('Could not parse saved settings');
        }
        return null;
    }

    /**
     * Check if auto-connect is enabled
     */
    shouldAutoConnect() {
        return sessionStorage.getItem(this.autoConnectKey) === 'true';
    }

    /**
     * Clear session storage
     */
    clearSession() {
        sessionStorage.removeItem(this.sessionStorageKey);
        sessionStorage.removeItem(this.autoConnectKey);
    }

    /**
     * Show connection result message
     */
    showResult(success, message, serverVersion = null) {
        const resultDiv = document.getElementById('connectionResult');
        if (!resultDiv) return;

        resultDiv.style.display = 'block';
        resultDiv.className = 'connection-result ' + (success ? 'success' : 'error');

        let html = this.escapeHtml(message);
        if (success && serverVersion) {
            html += `<span class="server-version">${this.escapeHtml(serverVersion)}</span>`;
        }
        resultDiv.innerHTML = html;
    }

    /**
     * Escape HTML to prevent XSS
     */
    escapeHtml(str) {
        if (!str) return '';
        return str
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
}

// Export singleton instance
window.connectionManager = new ConnectionManager();
