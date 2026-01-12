/**
 * Table Manager Module
 * Handles data table rendering, sorting, and pagination
 */
class TableManager {
    constructor() {
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
    }

    /**
     * Handle column sort
     */
    handleSort(tableId, col, dataCache, renderFn) {
        const state = this.sortState[tableId];
        if (!state) return;

        if (state.col === col) {
            state.dir = state.dir === 'asc' ? 'desc' : 'asc';
        } else {
            state.col = col;
            state.dir = 'desc';
        }

        this.updateSortIndicators(tableId);

        if (dataCache && renderFn) {
            renderFn(dataCache);
        }
    }

    /**
     * Update sort indicator icons
     */
    updateSortIndicators(tableId) {
        const table = document.getElementById(tableId);
        if (!table) return;

        const state = this.sortState[tableId];

        table.querySelectorAll('th.sortable').forEach(th => {
            th.classList.remove('sort-asc', 'sort-desc');
        });

        if (state.col) {
            const activeHeader = table.querySelector(`th[onclick*="'${state.col}'"]`);
            if (activeHeader) {
                activeHeader.classList.add(state.dir === 'asc' ? 'sort-asc' : 'sort-desc');
            }
        }
    }

    /**
     * Sort data array by column
     */
    sortData(data, col, dir) {
        if (!data || !col) return data;

        return [...data].sort((a, b) => {
            let aVal = a[col];
            let bVal = b[col];

            if (typeof aVal === 'string') {
                aVal = aVal.toLowerCase();
                bVal = (bVal || '').toLowerCase();
            }

            if (aVal === null || aVal === undefined) aVal = dir === 'asc' ? Infinity : -Infinity;
            if (bVal === null || bVal === undefined) bVal = dir === 'asc' ? Infinity : -Infinity;

            if (aVal < bVal) return dir === 'asc' ? -1 : 1;
            if (aVal > bVal) return dir === 'asc' ? 1 : -1;
            return 0;
        });
    }

    /**
     * Render empty state for table
     */
    renderEmptyState(tbody, colSpan, message = 'No data available') {
        tbody.innerHTML = `<tr><td colspan="${colSpan}" class="empty-state">${message}</td></tr>`;
    }

    /**
     * Render loading state for table
     */
    renderLoadingState(tbody, colSpan) {
        tbody.innerHTML = `<tr><td colspan="${colSpan}" class="loading-row"><span class="loading"></span> Loading...</td></tr>`;
    }

    /**
     * Render pagination controls
     */
    renderPagination(containerId, currentPage, totalPages, onPageChange) {
        const container = document.getElementById(containerId);
        if (!container || totalPages <= 1) {
            if (container) container.innerHTML = '';
            return;
        }

        let html = '<div class="pagination">';

        // Previous button
        html += `<button class="page-btn" ${currentPage <= 1 ? 'disabled' : ''} 
                         onclick="tableManager.changePage(${containerId}, ${currentPage - 1})">
                    &laquo; Prev
                 </button>`;

        // Page numbers
        const startPage = Math.max(1, currentPage - 2);
        const endPage = Math.min(totalPages, currentPage + 2);

        if (startPage > 1) {
            html += `<button class="page-btn" onclick="tableManager.changePage('${containerId}', 1)">1</button>`;
            if (startPage > 2) html += '<span class="page-ellipsis">...</span>';
        }

        for (let i = startPage; i <= endPage; i++) {
            html += `<button class="page-btn ${i === currentPage ? 'active' : ''}" 
                             onclick="tableManager.changePage('${containerId}', ${i})">${i}</button>`;
        }

        if (endPage < totalPages) {
            if (endPage < totalPages - 1) html += '<span class="page-ellipsis">...</span>';
            html += `<button class="page-btn" onclick="tableManager.changePage('${containerId}', ${totalPages})">${totalPages}</button>`;
        }

        // Next button
        html += `<button class="page-btn" ${currentPage >= totalPages ? 'disabled' : ''} 
                         onclick="tableManager.changePage('${containerId}', ${currentPage + 1})">
                    Next &raquo;
                 </button>`;

        html += '</div>';
        container.innerHTML = html;
    }
}

// Export singleton instance
window.tableManager = new TableManager();
