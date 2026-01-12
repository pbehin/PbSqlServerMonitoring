# SQL Server Monitoring Application

A comprehensive web-based application for real-time SQL Server performance monitoring. Track query performance, identify missing indexes, detect blocking sessions, and analyze lock contention.

![.NET](https://img.shields.io/badge/.NET-10.0-purple) ![License](https://img.shields.io/badge/license-MIT-blue) ![Tests](https://img.shields.io/badge/tests-26%20passing-brightgreen)

## Features

### 📊 Query Performance Monitoring
- **Top CPU Queries**: Identify queries consuming the most CPU resources
- **Top I/O Queries**: Find queries with highest logical reads/writes
- **Slowest Queries**: Detect long-running queries by execution time
- Execution count and performance trends
- Historical query data with configurable time ranges

### 🔍 Missing Index Analysis
- Automatic detection of missing indexes using SQL Server DMVs
- Impact score calculation to prioritize index creation
- Auto-generated `CREATE INDEX` statements
- User seeks and cost analysis

### 🚫 Blocking Session Detection
- Real-time blocking chain visualization
- Lead blocker identification
- Wait time tracking
- Query text inspection for blocked sessions
- Historical blocking data

### 🔒 Lock Analysis
- Current lock monitoring by session
- Lock type analysis (Exclusive, Shared, Update, etc.)
- Resource type breakdown (Table, Page, Key, etc.)
- Lock status monitoring

### 💻 Server Health Dashboard
- Active connections count
- Blocked processes alerting
- CPU and Memory usage
- Server uptime tracking
- Buffer cache hit ratio
- Performance history charts

## Prerequisites

- .NET 10.0 SDK or later
- SQL Server 2016 or later (LocalDB for development)
- SQL Server account with `VIEW SERVER STATE` permission

## Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/pbehin/PbSqlServerMonitoring.git
   cd PbSqlServerMonitoring
   ```

2. Build the application:
   ```bash
   dotnet build
   ```

3. Apply database migrations:
   ```bash
   dotnet ef database update
   ```

4. Run the application:
   ```bash
   dotnet run
   ```

5. Open your browser to `http://localhost:5000`

## Configuration

- `KeyRingPath` / `PB_MONITOR_KEYRING_PATH`: directory to persist Data Protection keys (recommended for scale-out/restarts).
- `PB_MONITOR_AUTO_MIGRATE`: set to `false` to skip automatic EF migrations (pending migrations are logged).
- `Persistence:RetentionDays` / `PB_MONITOR_RETENTION_DAYS`: metrics retention window (default 7, min 1, max 365).
- `Persistence:CleanupIntervalMinutes` / `PB_MONITOR_CLEANUP_INTERVAL_MINUTES`: cleanup cadence (default 60, min 5, max 1440).
- `AllowedOrigins`: CORS allowlist used in production.

### Connection Settings

You can configure the SQL Server connection through the web UI (Settings page) or via configuration files.

#### appsettings.json Configuration

```json
{
  "ConnectionStrings": {
    "PbMonitorConnection": "Server=(localdb)\\mssqllocaldb;Database=PbMonitorDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true",
    "SqlServer": "Server=YOUR_SERVER;Database=YOUR_DATABASE;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
  },
  "AllowedOrigins": [
    "https://localhost",
    "https://your-production-domain.com"
  ],
  "Security": {
    "EnableAuthentication": false,
    "AllowAnonymousInDevelopment": true,
    "ApiKey": "your-secret-api-key"
  },
  "Persistence": {
    "RetentionDays": 7,
    "CleanupIntervalMinutes": 60
  }
}
```

### Authentication

The API supports API Key authentication:

1. Set `Security:EnableAuthentication` to `true` in production
2. Set `Security:ApiKey` to a strong, unique value
3. Include the API key in requests:
   - Header: `X-API-Key: your-secret-api-key`
   - Query string: `?apiKey=your-secret-api-key`

In development mode, authentication is bypassed by default.

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `PB_MONITOR_KEYRING_PATH` | Path for Data Protection keys | System default |
| `PB_MONITOR_AUTO_MIGRATE` | Enable auto-migrations (`true`/`false`) | `true` |
| `PB_MONITOR_RETENTION_DAYS` | Days to keep historical data | `7` |
| `PB_MONITOR_CLEANUP_INTERVAL_MINUTES` | Cleanup interval | `60` |

## API Endpoints

### Health & Metrics
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Server health status |
| `/api/metrics/history` | GET | Historical metrics |
| `/api/metrics/latest` | GET | Latest metric data point |
| `/api/metrics/buffer-health` | GET | Internal buffer health |

### Query Performance
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/queries/top-cpu` | GET | Top queries by CPU usage |
| `/api/queries/top-io` | GET | Top queries by I/O |
| `/api/queries/slowest` | GET | Slowest queries |
| `/api/queries/active-cpu` | GET | Currently running high-CPU queries |
| `/api/queries/history` | GET | Historical query data |

### Blocking & Locks
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/blocking/active` | GET | Active blocking sessions |
| `/api/locks/current` | GET | Current locks |

### Settings
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/settings/connection` | GET | Get connection info (without password) |
| `/api/settings/connection` | POST | Update connection settings |
| `/api/settings/connection/test` | POST | Test connection without saving |
| `/api/settings/connection` | DELETE | Clear connection settings |

## SQL Server Permissions

The monitoring account needs the following permissions:

```sql
GRANT VIEW SERVER STATE TO [YourUser];
GRANT VIEW DATABASE STATE TO [YourUser];
```

## Project Structure

```
PbSqlServerMonitoring/
├── Controllers/           # API Controllers
│   ├── HealthController.cs
│   ├── QueriesController.cs
│   ├── MetricsController.cs
│   ├── IndexesController.cs
│   ├── BlockingController.cs
│   ├── LocksController.cs
│   ├── RunningController.cs
│   └── SettingsController.cs
├── Data/                  # Database Context
│   └── MonitorDbContext.cs
├── Models/                # Data Models
│   ├── MonitoringModels.cs
│   ├── PersistenceEntities.cs
│   └── MetricsDtos.cs
├── Services/              # Business Logic
│   ├── BaseMonitoringService.cs
│   ├── ConnectionService.cs
│   ├── MetricsCollectionService.cs
│   ├── QueryPerformanceService.cs
│   ├── MissingIndexService.cs
│   ├── BlockingService.cs
│   ├── RunningQueriesService.cs
│   ├── ServerHealthService.cs
│   ├── SqlPersistenceService.cs
│   └── IMetricsPersistenceService.cs
├── Migrations/            # EF Core Migrations
├── wwwroot/               # Frontend Dashboard
│   ├── index.html
│   ├── styles.css
│   └── app.js
├── PbSqlServerMonitoring.Tests/  # Unit Tests
│   ├── Controllers/
│   ├── Models/
│   └── Services/
├── Program.cs             # Application entry point
└── appsettings.json       # Configuration
```

## Security Considerations

### Production Deployment

⚠️ **Important**: Before deploying to production:

1. **Configure CORS Origins**: Update `AllowedOrigins` in `appsettings.json` with your actual domains
2. **Swagger is disabled in Production**: API documentation is only available in Development mode
3. **Add Authentication**: Consider adding authentication for production use
4. **Use HTTPS**: Always use HTTPS in production
5. **Secure Connection Strings**: Use environment variables or Azure Key Vault for sensitive configuration

### Connection Security

- Connection strings are encrypted at rest using Data Protection API
- Passwords are never logged or returned in API responses
- All SQL queries use parameterized queries to prevent SQL injection

## Development

### Running Tests

```bash
dotnet test
```

### Running in Development Mode

```bash
dotnet run --environment Development
```

In development mode:
- Swagger UI is available at `/swagger`
- CORS allows any origin
- Detailed error messages are shown

## Architecture

### Low-Impact Monitoring

All monitoring queries use:
- `READ UNCOMMITTED` isolation level to avoid locking
- Short query timeouts (10-30 seconds) to prevent pressure on busy servers
- `NOLOCK` hints where appropriate
- Capped result sets (max 100 records)

### Data Collection

- Metrics are collected every 3 seconds
- In-memory buffer stores 15 minutes of recent data
- SQL Server stores historical data with configurable retention
- Background cleanup removes old data automatically

## Screenshots

The dashboard features a modern dark theme with:
- Real-time server health statistics
- Interactive data tables with sorting
- Performance history charts
- Query detail modals
- Navigation between monitoring sections

## Technologies Used

- **Backend**: ASP.NET Core Web API (.NET 10)
- **ORM**: Entity Framework Core 10
- **Frontend**: Vanilla JavaScript, HTML5, CSS3
- **Charts**: Chart.js 4.4
- **Database**: SQL Server DMVs (Dynamic Management Views)
- **Testing**: xUnit, Moq
- **API Docs**: Swagger/OpenAPI (Development only)

## License

MIT License - feel free to use and modify for your needs.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Guidelines

1. Write tests for new functionality
2. Follow existing code style
3. Update documentation as needed
4. Ensure all tests pass before submitting
