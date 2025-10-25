# Project Guidelines

These guidelines define mandatory practices for this repository.

- All database operations must be wrapped in a Repository.
    - Do not access the DbContext directly from controllers, services, or views.
    - Create an appropriate repository interface and implementation in the Repositories folder and inject it where needed.
    - When you make changes to the database schema, DO NOT create migrations in the Migrations folder or update the DB Snapshot.
  
- Dependency injection and lifetimes.
    - Register repositories, DbContext-backed services, and other application services in DI as Scoped unless there is a clear reason to do otherwise.
    - Services must receive all dependencies via constructor injection; do not use service locators inside business logic.

- Error handling.
    - Validate inputs early; return informative error messages in structured results or view models when appropriate.
    - Avoid leaking internal exceptions; log them and return user-friendly messages instead.

- UI Views and MVC structure.
    - Use the shadcn library.
    - Major views must have their own dedicated MVC controller and view folder.
    - Controllers for major views MUST be separate classes inheriting from WineSurferControllerBase (do not add major view actions to generic or unrelated controllers).
    - Name controllers <Feature>Controller and place them in Controllers/.
    - Each major view must have its own Views/<Feature>/Index.cshtml as the entry point (do not reuse another feature's Index.cshtml).
    - Keep feature-specific partials and pages under Views/<Feature>/...
    - Share layout or truly cross-cutting partials only via Views/Shared when appropriate.
    - In Razor views, escape literal `@` in static content (like CSS `@media`, JS templates, or email addresses) by writing `@@` so Razor doesn’t parse it as code.

Feel free to extend this document with additional conventions (naming, testing, error handling) as the project evolves.
