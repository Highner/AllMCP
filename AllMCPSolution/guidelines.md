# Project Guidelines

These guidelines define mandatory practices for this repository.

- All database operations must be wrapped in a Repository.
    - Do not access the DbContext directly from controllers, services, tools, or views.
    - Create an appropriate repository interface and implementation in the Repositories folder and inject it where needed.
    - When you make changes to the database schema, DO NOT create migrations in the Migrations folder or update the DB Snapshot.

- Tool interfaces and exposure (MANDATORY).
    - Every executable tool class MUST implement both interfaces: IToolBase and IMcpTool.
    - IResourceProvider is OPTIONAL and only required if the tool serves UI/assets via MCP resources. Do not implement it unless you actually serve resources.
    - Decorate tool classes with [McpTool("name", "description")] so they are discoverable by both the OpenAPI manifest and the MCP registry.
    - Keep a single source of truth (DRY):
        - Put business logic in IToolBase.ExecuteAsync(Dictionary<string, object>? parameters).
        - Implement IMcpTool.RunAsync(...) by converting the MCP arguments to Dictionary<string, object> and delegating to ExecuteAsync.

- Input/Output schemas for tools.
    - For OpenAPI: implement GetOpenApiSchema() returning the OpenAPI request body schema for the tool.
    - For MCP: IMcpTool.GetDefinition().InputSchema MUST be a valid JSON Schema for input arguments (type: "object", properties, required), NOT an OpenAPI operation object.
    - Prefer reusing the same property map in both paths (e.g., ParameterHelpers.CreateOpenApiProperties(...)).
    - Tools should return structured results as plain POCO/anonymous objects; the MCP implementation should set CallToolResult.StructuredContent accordingly.

- Dependency injection and lifetimes.
    - Register repositories, DbContext-backed services, and all tools in DI as Scoped unless there is a clear reason to do otherwise.
    - Tools must receive all dependencies via constructor injection; do not use service locators inside business logic.
    - MCP tool activation is DI-backed. Ensure all IMcpTool implementers are registered so the McpToolRegistry can resolve them from a scope.

- Discovery and registration.
    - The solution scans for IMcpTool implementers and registers them; ensure your tool class is non-abstract and public.
    - Tool names MUST be unique across the application. Use stable, lowercase, snake_case names.

- Tool widget payload watcher (async hydration rules)
    - Always wire both event and polling paths. First subscribe to `openai.subscribeToToolOutput` or `openai.onToolOutput`; additionally run a lightweight 300ms poll that reads `window.openai?.toolOutput ?? window.openai?.message?.toolOutput` so hydration still works if the host never fires events. Clear the interval on teardown (e.g., `beforeunload`).
    - Detect real content changes with a stable stamp. Build a `stamp` from `(id || $id) + '|' + (timestamp || time) + '|' + safeStringify(candidate).slice(0, 2048)`. Only call `handlePayload(candidate)` when the stamp differs from the last one to avoid duplicate renders.
    - Use cycle-safe stringify for hashing. Implement `safeStringify` with a `WeakSet` to skip already-seen objects during `JSON.stringify`, preventing crashes on cyclic structures and keeping the hash bounded for performance.

- Safety and metadata.
    - Populate IToolBase.SafetyLevel when applicable and (optionally) mirror it in the MCP Tool.Meta object.
    - If you expose UI via IResourceProvider, ensure Resource.Uri values are stable and referenced from Tool.Meta (e.g., "openai/outputTemplate").

- Error handling.
    - Validate inputs early; return informative error messages in structured results when appropriate.
    - Throw McpException with a suitable McpErrorCode for MCP-specific validation errors; do not leak internal exceptions.

Feel free to extend this document with additional conventions (naming, testing, error handling) as the project evolves.