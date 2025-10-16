# Project Guidelines

These guidelines define mandatory practices for this repository.

- All database operations must be wrapped in a Repository.
  - Do not access the DbContext directly from controllers, services, tools, or views.
  - Create an appropriate repository interface and implementation in the Repositories folder and inject it where needed.

- All MCP tools must be built using the IMcpTool interface.
  - Implement the required contract on your tool class.
  - Ensure your tool is properly discoverable/registered per the project’s MCP tooling setup.

Feel free to extend this document with additional conventions (naming, testing, error handling) as the project evolves.