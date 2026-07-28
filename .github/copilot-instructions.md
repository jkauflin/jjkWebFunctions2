# Project Overview

This project is an Azure Functions backend to handle queued tasks from an Event Grid.  It is part of an azure static web application for a homeowners association.  That static web app is located at GrhaWeb on GitHub.  This function handles request to send emails.

# Project Overview

This project is an Azure Function API backend for an azure static web application for a personal website 
It provides public access to personal images, videos, music, and documents

## Folder Structure

- `/Model`: Contains the source code for the data model classes.

## Libraries and Frameworks

The frontend is an azure static web app written using:
-  HTML5, CSS3, and JavaScript (ES6+) modules (.mjs files)
-  Bootstrap 5, including tab navigation, modals, and forms.
-  Font Awesome 4 for icons.

The backend API is in a separate project written using:
- C# with Azure Functions for serverless API endpoints.
- Functions run as .NET 10 dotnet-isolated process.
- Uses Azure Functions HTTP triggers for API endpoints which make calls to functions in DbCommon.cs to handle database operations, and other common processing tasks.
- Uses Azure Functions Timer triggers for scheduled tasks.
- Cosmos DB for data storage.
- Azure BLOB storage for file uploads

Local development uses:
- Azure Static Web Apps CLI for running the frontend locally.
- Azure Functions Core Tools for running the backend locally.

## Coding Standards

- Use proper indentation (4 spaces).
- Use camelCase for variable and function names with lowercase first letter to match JavaScript conventions and JSON transformations.
- Add new functions to WebApi.cs and DbCommon.cs as needed but put them at the bottom of the class in the file.
- Use async/await for asynchronous operations in C#
- Use try/catch blocks for error handling in C# but not in DbCommon.cs which should throw exceptions to be caught in WebApi.cs.
- Use comments to explain complex logic and document functions.

## UI guidelines

- Responsive web design supporting mobile and desktop devices.
- Use Bootstrap 5 for layout and styling.
- Data for backend API should be fetched using the Fetch API, and passed using FormData.
- Use modals for user interactions like admin actions.
- Use tabs for navigation between different sections of the application.
- Use Font Awesome 4 for icons.
