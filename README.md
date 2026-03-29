# LinkShortenerApp_Blazor

A simple URL shortener built with ASP.NET Core Blazor and SQL Server.

## Features

- Shorten long URLs
- Custom short codes
- Click tracking
- REST API

## Tech Stack

- .NET 8.0
- Blazor Server
- SQL Server
- Entity Framework Core

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- SQL Server

### Installation

1. Clone the repository
2. Update connection string in ppsettings.json
3. Run dotnet ef database update
4. Run dotnet run
5. Open https://localhost:7205

## API Usage

POST /api/shorten
{
    "originalUrl": "https://example.com",
    "customCode": "mycode"
}

## Author

Irfanudheen K

## License

MIT
