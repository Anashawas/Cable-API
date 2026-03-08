# Cable - EV Charging Station Management System

## Project Overview
Cable is a comprehensive Electric Vehicle (EV) Charging Station Management System that serves as a marketplace platform connecting EV owners with charging point providers. Built for the Middle Eastern market with full Arabic language support.

## Architecture
- **Clean Architecture** with CQRS pattern
- **.NET 9.0** with ASP.NET Core
- **Entity Framework Core** with SQL Server
- **Firebase** for authentication and push notifications
- **JWT** token-based security

## Key Features
- Charging station discovery and management
- User authentication (Email/Password, Google OAuth)
- Vehicle registration and compatibility matching
- Rating and review system
- Complaint management
- File upload and secure serving
- Push notifications
- Bilingual support (English/Arabic)

## Development Commands
```bash
# Build the solution
dotnet build

# Run the API
dotnet run --project WebApi

# Run tests (if available)
dotnet test

# Database migrations
dotnet ef database update --project Infrastructrue --startup-project WebApi
```

## Project Structure
- **Domain/**: Core business entities
- **Application/**: Business logic with CQRS using MediatR
- **Infrastructrue/**: Database, Firebase, file storage
- **Cable.WebApi/**: REST API endpoints
- **Cable.Core/**: Shared utilities and exceptions
- **Cable.Security.***: Authentication and encryption services

## Database
- Uses Entity Framework Core with SQL Server
- Configurations in `Infrastructrue/Persistence/Configurations/`

## Authentication
- Firebase Authentication for mobile clients
- JWT tokens for API access
- Role-based authorization system

## Localization
- Support for English and Arabic (RTL)
- Resource files in each project's Localization folder

## Notes for Claude
- This is an EV charging station management platform
- Focus on defensive security practices
- Follow existing code conventions and patterns
- Use the established CQRS pattern with MediatR
- Maintain bilingual support when making changes

## Startup Rule (MUST follow at the beginning of every conversation)
Before doing anything else, Claude MUST silently explore and understand the project by:
1. Read all `.md` files in the project root
2. Explore the full project structure and design patterns
3. Understand the current workflow and business logic
4. Study custom exceptions implementation (in Cable.Core)
5. Study extension methods used in minimal API
6. Study how routes are implemented with CORS (use UserRoutes as reference)
7. Understand the CQRS command/query pattern with MediatR as used here

This exploration should happen automatically without the user asking. Do NOT skip this step. After exploration, confirm readiness with a brief summary of what was reviewed.