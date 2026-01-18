# SQL Server Monitoring Stack

A Podman-based monitoring solution for SQL Server using **sql_exporter**, **Prometheus**, and **Grafana**.

## Architecture

```
┌────────────────────┐
│   SQL Server       │
│   (Your Database)  │
└────────┬───────────┘
         │ SQL Queries
         ▼
┌────────────────────┐
│  sql_exporter      │  ← Port 9399
│  (Metrics Export)  │
└────────┬───────────┘
         │ HTTP /metrics
         ▼
┌────────────────────┐
│   Prometheus       │  ← Port 9090
│   (Time Series DB) │
└────────┬───────────┘
         │ PromQL
         ▼
┌────────────────────┐
│   Grafana          │  ← Port 3000
│   (Visualization)  │
└────────────────────┘
```

## Prerequisites

- [Podman](https://podman.io/) installed
- [podman-compose](https://github.com/containers/podman-compose) installed
- SQL Server accessible from host machine
- SQL user with `VIEW SERVER STATE` permission

## Quick Start

### 1. Configure Environment

```powershell
# Copy the example environment file
cp .env.example .env

# Edit .env with your SQL Server connection details
notepad .env
```

**Required settings in `.env`:**
```ini
SQLSERVER_HOST=host.containers.internal  # or your SQL Server IP
SQLSERVER_PORT=1433
SQLSERVER_USER=sa
SQLSERVER_PASSWORD=YourPassword
```

### 2. Start the Stack

```powershell
podman-compose up -d
```

### 3. Access Services

| Service | URL | Credentials |
|---------|-----|-------------|
| **Grafana** | http://localhost:3000 | admin / admin |
| **Prometheus** | http://localhost:9090 | - |
| **sql_exporter** | http://localhost:9399/metrics | - |

### 4. View Dashboard

1. Open Grafana at http://localhost:3000
2. Login with admin/admin
3. Navigate to **Dashboards → SQL Server → SQL Server Monitoring**

## Metrics Collected

| Metric | Description |
|--------|-------------|
| `mssql_cpu_usage_percent` | SQL Server CPU utilization |
| `mssql_connections_total` | Active connections per database |
| `mssql_page_life_expectancy_seconds` | Buffer pool efficiency |
| `mssql_buffer_cache_hit_ratio` | Cache hit percentage |
| `mssql_batch_requests_total` | Batch requests counter |
| `mssql_deadlocks_total` | Deadlock count |
| `mssql_memory_total_bytes` | Memory usage |
| `mssql_database_size_bytes` | Database sizes |
| `mssql_wait_time_ms` | Wait statistics by type |
| `mssql_active_queries_total` | Currently running queries |

## Alerts Configured

- High CPU usage (>80% for 5 min)
- Low Page Life Expectancy (<300s)
- Low Buffer Cache Hit Ratio (<90%)
- Deadlocks detected
- High connection count (>500)
- SQL Exporter down

## Management Commands

```powershell
# View logs
podman-compose logs -f

# Stop all services
podman-compose down

# Restart a specific service
podman-compose restart sql_exporter

# Update images
podman-compose pull
podman-compose up -d
```

## Troubleshooting

### Cannot connect to SQL Server

1. Ensure SQL Server allows TCP/IP connections
2. Check Windows Firewall allows port 1433
3. For localhost SQL Server, use `host.containers.internal`
4. Verify SQL authentication is enabled

### No metrics appearing

```powershell
# Check sql_exporter logs
podman-compose logs sql_exporter

# Test metrics endpoint directly
curl http://localhost:9399/metrics
```

### Prometheus not scraping

1. Open http://localhost:9090/targets
2. Check if sql_exporter target shows "UP"
3. Review error messages if "DOWN"

## File Structure

```
monitoring/
├── podman-compose.yml          # Service orchestration
├── .env.example                # Environment template
├── .env                        # Your configuration (gitignored)
├── sql_exporter/
│   └── sql_exporter.yml        # SQL metrics collectors
├── prometheus/
│   ├── prometheus.yml          # Scrape configuration
│   └── alerts/
│       └── sql_alerts.yml      # Alert rules
└── grafana/
    └── provisioning/
        ├── datasources/
        │   └── prometheus.yml  # Auto-configure datasource
        └── dashboards/
            ├── dashboards.yml  # Dashboard provisioner
            └── sql_server.json # Pre-built dashboard
```

## License

MIT License
