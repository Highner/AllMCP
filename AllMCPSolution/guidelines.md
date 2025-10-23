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

- UI Views.
    - Use the shadcn library. 

Feel free to extend this document with additional conventions (naming, testing, error handling) as the project evolves.
