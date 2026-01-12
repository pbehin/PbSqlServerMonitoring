/**
 * Utility Functions Module
 * Common helper functions used across the application
 */
const Utils = {
    /**
     * Format number with locale-specific separators
     */
    formatNumber(num) {
        if (num === null || num === undefined || isNaN(num)) return '-';
        return num.toLocaleString();
    },

    /**
     * Truncate text with ellipsis.
     * Note: Output is HTML-escaped for safe use in innerHTML.
     */
    truncateText(text, maxLength = 50) {
        if (!text) return '';
        const truncated = text.length <= maxLength ? text : text.substring(0, maxLength - 3) + '...';
        return this.escapeHtml(truncated);
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
     * Format date/time for display
     */
    formatDateTime(dateStr) {
        if (!dateStr) return '-';
        const date = new Date(dateStr);
        if (isNaN(date.getTime())) return dateStr;

        return date.toLocaleString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            hour12: false
        });
    },

    /**
     * Format uptime from TimeSpan string
     */
    formatUptime(uptimeStr) {
        if (!uptimeStr) return '-';

        const parts = uptimeStr.split(/[:.]/);
        if (parts.length < 3) return uptimeStr;

        const days = parseInt(parts[0]) || 0;
        const hours = parseInt(parts[1]) || 0;
        const minutes = parseInt(parts[2]) || 0;

        if (days > 0) {
            return `${days}d ${hours}h ${minutes}m`;
        } else if (hours > 0) {
            return `${hours}h ${minutes}m`;
        } else {
            return `${minutes}m`;
        }
    },

    /**
     * Format datetime-local input value
     */
    formatDateTimeLocal(date) {
        const pad = n => n.toString().padStart(2, '0');
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    },

    /**
     * Debounce function calls
     */
    debounce(func, wait) {
        let timeout;
        return function executedFunction(...args) {
            const later = () => {
                clearTimeout(timeout);
                func(...args);
            };
            clearTimeout(timeout);
            timeout = setTimeout(later, wait);
        };
    },

    /**
     * Throttle function calls
     */
    throttle(func, limit) {
        let inThrottle;
        return function (...args) {
            if (!inThrottle) {
                func.apply(this, args);
                inThrottle = true;
                setTimeout(() => inThrottle = false, limit);
            }
        };
    },

    /**
     * Create loading button state
     */
    setButtonLoading(button, isLoading, originalText = null) {
        const btnText = button.querySelector('.btn-text');
        const spinner = button.querySelector('.loading');

        if (isLoading) {
            if (btnText && originalText) btnText.textContent = originalText;
            if (spinner) spinner.style.display = 'inline-block';
            button.disabled = true;
        } else {
            if (spinner) spinner.style.display = 'none';
            button.disabled = false;
        }
    },

    /**
     * Update last refresh time display
     */
    updateLastRefreshTime() {
        const el = document.getElementById('lastUpdated');
        if (el) {
            el.textContent = new Date().toLocaleTimeString();
        }
    }
};

// Export to global scope
window.Utils = Utils;
