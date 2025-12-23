# Cable - Electric Vehicle Charging Station Management System

![.NET](https://img.shields.io/badge/.NET-9.0-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)

**Cable** is a comprehensive Electric Vehicle (EV) Charging Station Management System that connects EV owners with charging point providers. It provides a complete ecosystem for managing charging stations, user accounts, vehicle compatibility, ratings, and mobile notifications.

## 🚗 Overview

Cable serves as a marketplace platform similar to ChargePoint or PlugShare, designed specifically for the Middle Eastern market with full Arabic language support. The system allows users to:

- **Find** nearby EV charging stations with detailed information
- **Rate and Review** charging points based on their experience
- **Manage** their electric vehicles and charging history
- **Upload** photos and attachments for charging stations
- **Receive** push notifications about charging status and updates
- **Report** issues through an integrated complaint system

## 🏗️ Architecture

The project follows **Clean Architecture** principles with **CQRS** pattern implementation:

```
├── Domain/              # Core business entities and rules
├── Application/         # Use cases, commands, queries, and business logic
├── Infrastructure/      # External concerns (database, Firebase, file storage)
├── WebApi/             # RESTful API endpoints and controllers
├── Cable.Core/         # Shared exceptions and utilities
├── Cable.Security.*/   # Authentication and security services
└── Cable.WebApi.OpenAPI/ # API documentation configuration
```

## 🛠️ Technology Stack

### Backend
- **.NET 9.0** - Latest .NET framework
- **ASP.NET Core** - Web API with minimal APIs
- **Entity Framework Core** - ORM with SQL Server
- **MediatR** - CQRS and mediator pattern implementation
- **FluentValidation** - Request validation
- **AutoMapper** - Object-to-object mapping

### Security & Authentication
- **JWT Bearer Authentication** - Token-based security
- **Firebase Authentication** - Google OAuth integration
- **Custom Password Hashing** - Secure password storage
- **Triple DES Encryption** - Data encryption
- **Role-based Authorization** - Permission management

### External Services
- **Firebase Cloud Messaging** - Push notifications
- **Google OAuth** - Social authentication
- **SQL Server** - Primary database
- **Hangfire** - Background job processing
- **NetTopologySuite** - Geographic data handling

### Development Tools
- **OpenAPI/Swagger** - API documentation with Scalar UI
- **Hangfire Dashboard** - Job monitoring
- **Comprehensive Logging** - Console and event logging
- **CORS Support** - Cross-origin request handling

## 🚀 Getting Started

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (or SQL Server Express)
- [Firebase Account](https://firebase.google.com/) for authentication and notifications
- [Google Cloud Console](https://console.cloud.google.com/) for OAuth setup

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-username/cable.git
   cd cable
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Configure the database**
   
   Update the connection string in `WebApi/appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "Cable": "Server=your-server;Initial Catalog=Cable;Integrated Security=true;"
     }
   }
   ```

4. **Set up Firebase**
   
   - Create a Firebase project
   - Download the service account JSON file
   - Place it in `WebApi/Firebase/ServiceAccount/ServiceAccount.json`
   - Update Firebase configuration in `appsettings.json`

5. **Configure Google OAuth**
   
   - Create OAuth 2.0 credentials in Google Cloud Console
   - Add client IDs to `appsettings.json`:
   ```json
   {
     "Google": {
       "ClientIds": [
         "your-client-id.apps.googleusercontent.com"
       ]
     }
   }
   ```

6. **Set up file storage**
   
   Create the upload directory and update the path in `appsettings.json`:
   ```json
   {
     "File": {
       "FileUploadPath": "C:\\Attachments",
       "ServerUrl": "https://localhost:7000"
     }
   }
   ```

7. **Run the application**
   ```bash
   cd WebApi
   dotnet run
   ```

   The API will be available at:
   - **HTTPS**: `https://localhost:7000`
   - **HTTP**: `http://localhost:5000`
   - **Swagger UI**: `https://localhost:7000/scalar/v1`
   - **Hangfire Dashboard**: `https://localhost:7000/Cable-Jobs-Dashboard`

## 📡 API Endpoints

### Authentication
```
POST   /api/users/authenticate           # Email/Password login
POST   /api/users/login-by-google        # Google OAuth login
POST   /api/users/login-by-token         # Token-based login
POST   /api/users/refresh-access         # Refresh JWT token
```

### User Management
```
GET    /api/users/GetAllUsers           # List all users
POST   /api/users/AddUser               # Create new user
PUT    /api/users/{id}                  # Update user
PATCH  /api/users/{id}/change-password  # Change password
DELETE /api/users/{id}                  # Delete user
```

### Charging Points
```
POST   /api/charging-points/GetAllChargingPoints    # List with filters
GET    /api/charging-points/GetChargingPointById/{id} # Get specific point
POST   /api/charging-points/AddChargingPoint        # Add new point
PUT    /api/charging-points/UpdateChargingPoint/{id} # Update point
PATCH  /api/charging-points/UpdateChargingPointStatus/{id} # Update status
DELETE /api/charging-points/DeleteChargingPoint/{id} # Delete point
```

### File Management
```
GET    /api/files/{folder}/{fileName}               # Secure file serving
GET    /api/files/GetAllBannerAttachmentsById/{id}  # Get attachments
```

For complete API documentation, visit the Swagger UI at `/scalar/v1` when running the application.

## 🗄️ Database Schema

### Core Entities

- **UserAccount** - User profiles with authentication and location
- **ChargingPoint** - EV charging stations with geographic data
- **ChargingPointType** - Categories and types of charging stations
- **PlugType** - Different charging connector standards
- **UserCar** - User's registered electric vehicles
- **Rate** - User ratings and reviews for charging points
- **UserComplaint** - Support tickets and issue reporting
- **Banner** - Promotional content and advertisements
- **NotificationToken** - Push notification device management

### Relationships

```
UserAccount (1) ──── (N) ChargingPoint
UserAccount (1) ──── (N) UserCar
UserAccount (1) ──── (N) Rate
ChargingPoint (1) ── (N) ChargingPlug
ChargingPoint (1) ── (N) ChargingPointAttachment
```

## 🔧 Configuration

### Environment Settings

The application supports multiple environments with specific configuration files:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Staging.json` - Staging environment
- `appsettings.Production.json` - Production settings

### Key Configuration Sections

```json
{
  "Database": {
    "CommandTimeOutInSeconds": 180,
    "DefaultSchema": "dbo",
    "EnableSensitiveDataLogging": false
  },
  "Token": {
    "AccessTokenExpiresAfter": "10.00:00:00",
    "RefreshTokenExpiresAfter": "30.00:00:00"
  },
  "File": {
    "LimitSize": 5242880,
    "AllowedExtensions": [".jpg", ".jpeg", ".png"]
  }
}
```

## 🌐 Localization

The application supports multiple languages:

- **English** (default)
- **Arabic** with RTL support

Language is determined by the `Accept-Language` header or can be explicitly set through the API.

## 🔒 Security Features

- **JWT Authentication** with refresh token support
- **Role-based Authorization** with granular permissions
- **Firebase Integration** for secure Google authentication
- **Password Hashing** with custom implementation
- **File Upload Security** with type and size validation
- **Request Validation** using FluentValidation
- **Secure File Serving** with user-based access control

## 📱 Mobile Integration

The API is designed to support mobile applications with:

- **Push Notifications** via Firebase Cloud Messaging
- **Image Upload** for charging point photos
- **Offline-first** data structures
- **Location-based** services with geographic queries
- **Token Management** for seamless authentication

## 🏃‍♂️ Development

### Running in Development

```bash
# Start the API
cd WebApi
dotnet run --environment Development

# View logs
dotnet run --verbosity detailed

# Run with HTTPS
dotnet run --urls="https://localhost:7000;http://localhost:5000"
```

### Available Scripts

```bash
# Build solution
dotnet build

# Run tests (if available)
dotnet test

# Publish for deployment
dotnet publish -c Release -o ./publish

# Generate database scripts (if migrations are added)
dotnet ef migrations script
```

### Development Tools

- **Hangfire Dashboard**: Monitor background jobs at `/Cable-Jobs-Dashboard`
- **Swagger/Scalar UI**: API documentation at `/scalar/v1`
- **Health Checks**: Application health monitoring
- **Request/Response Logging**: Comprehensive request tracing

## 🚀 Deployment

### Production Checklist

1. **Database Setup**
   - Ensure SQL Server is properly configured
   - Set up database with appropriate permissions
   - Configure connection strings for production

2. **Security Configuration**
   - Generate secure JWT signing keys
   - Configure Firebase production credentials
   - Set up Google OAuth for production domain
   - Ensure HTTPS is properly configured

3. **File Storage**
   - Set up production file storage location
   - Configure appropriate file permissions
   - Ensure backup strategy for uploaded files

4. **Environment Variables**
   - Set `ASPNETCORE_ENVIRONMENT=Production`
   - Configure production connection strings
   - Set up proper logging levels

### Docker Deployment (Optional)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

COPY . ./
RUN dotnet restore
RUN dotnet publish -c Release -o out

FROM base AS final
COPY --from=build /out .
ENTRYPOINT ["dotnet", "WebApi.dll"]
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Guidelines

- Follow C# coding conventions
- Implement proper error handling
- Add unit tests for new features
- Update documentation for API changes
- Ensure localization support for new strings

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📞 Support

For support and questions:

- **Issues**: [GitHub Issues](https://github.com/your-username/cable/issues)
- **Documentation**: [API Documentation](https://localhost:7000/scalar/v1)
- **Email**: support@cable-app.com

## 🙏 Acknowledgments

- Built with [.NET 9](https://dotnet.microsoft.com/)
- Authentication powered by [Firebase](https://firebase.google.com/)
- Geographic data handled by [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite)
- Background jobs managed by [Hangfire](https://www.hangfire.io/)
- API documentation with [Scalar](https://github.com/scalar/scalar)

---

**Cable** - Connecting the future of electric vehicle charging 🔌⚡