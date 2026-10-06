# UserManagementAPI

A User Management API for TechHive Solutions' HR and IT departments, built with ASP.NET Core (.NET 10). It supports creating, retrieving, updating and deleting user records.

## Running

```bash
dotnet run --project UserManagementAPI --launch-profile http
```

The API listens on `http://localhost:5136`. In Development, the OpenAPI document is at `/openapi/v1.json`.

Data is kept in memory and resets on restart. The API starts with two seeded users: Alice (ID 1, HR) and Bob (ID 2, IT).

### Authentication

Every `/api` request needs an `Authorization: Bearer <token>` header. Tokens are configured per client under `Authentication:ApiTokens`. Each token must be at least 32 characters, and the app refuses to start otherwise. `UserManagementAPI/appsettings.Development.json` contains two development tokens:

| Client | Token |
|---|---|
| `hr-portal` | `2JFY8QmsMU2T4G0zeWLatEwQIlJ3vUmfFvZFcd0Bx7M` |
| `it-admin` | `Oa4Gt4xAQZCtqUtfmtihgiLpxR1BKnNRpkOsyd3tkWc` |

```bash
curl -H "Authorization: Bearer 2JFY8QmsMU2T4G0zeWLatEwQIlJ3vUmfFvZFcd0Bx7M" http://localhost:5136/api/users
```

`UserManagementAPI/appsettings.json` (production) has no tokens, so every `/api` request is rejected until tokens are supplied. Supply them through environment variables or a secret store, never in source control. For example:

```bash
Authentication__ApiTokens__hr-portal=<token>
```

## Project structure

| Path | Purpose |
|---|---|
| [`Program.cs`](UserManagementAPI/Program.cs) | Host setup, service registration and the middleware pipeline |
| [`Models/User.cs`](UserManagementAPI/Models/User.cs) | Immutable user record (`Id`, `FirstName`, `LastName`, `Email`, `Department`, `CreatedAt`, `UpdatedAt`) |
| [`Models/UserRequest.cs`](UserManagementAPI/Models/UserRequest.cs) | POST/PUT payload with validation attributes |
| [`Services/IUserService.cs`](UserManagementAPI/Services/IUserService.cs) | Data-access abstraction with atomic create/update results |
| [`Services/InMemoryUserService.cs`](UserManagementAPI/Services/InMemoryUserService.cs) | Thread-safe in-memory implementation, with an email index |
| [`Middleware/ErrorHandlingMiddleware.cs`](UserManagementAPI/Middleware/ErrorHandlingMiddleware.cs) | Global try/catch: turns unhandled exceptions into logged 500 responses |
| [`Middleware/TokenAuthenticationMiddleware.cs`](UserManagementAPI/Middleware/TokenAuthenticationMiddleware.cs) | Bearer-token validation; 401 for missing or invalid tokens |
| [`Middleware/TokenAuthenticationOptions.cs`](UserManagementAPI/Middleware/TokenAuthenticationOptions.cs) | Token configuration (`Authentication:ApiTokens`) |
| [`Middleware/RequestLoggingMiddleware.cs`](UserManagementAPI/Middleware/RequestLoggingMiddleware.cs) | Audit log: method, path, status code, duration, client |
| [`Controllers/UsersController.cs`](UserManagementAPI/Controllers/UsersController.cs) | CRUD endpoints |
| [`UserManagementAPI.Tests/`](UserManagementAPI.Tests/) | xUnit integration tests (73 cases) |

## Endpoints

| Method | Route | Success | Errors |
|---|---|---|---|
| GET | `/api/users?page=1&pageSize=50` | 200 + page of users + `X-Total-Count` header | 400 invalid paging |
| GET | `/api/users/{id}` | 200 + user | 404 not found |
| POST | `/api/users` | 201 + user + `Location` header | 400 invalid body, 409 duplicate email |
| PUT | `/api/users/{id}` | 200 + updated user | 400 invalid body, 404 not found, 409 duplicate email |
| DELETE | `/api/users/{id}` | 204 | 404 not found |

Any `/api` endpoint can return 401 if the token is missing or invalid, and 500 if something unexpected fails.

Every error response uses the same `application/problem+json` shape (RFC 9457), with a short `error` message and a `traceId`:

```json
{ "type": "...", "title": "Internal server error.", "status": 500, "detail": "...", "traceId": "00-...", "error": "Internal server error." }
```

In Development only, `GET /api/debug/throw` throws an exception on purpose, so you can test error handling. It still requires a token.

Example request body for POST and PUT:

```json
{
  "firstName": "Carol",
  "lastName": "Diaz",
  "email": "carol.diaz@techhive.com",
  "department": "Finance"
}
```

### Validation rules

All values are trimmed before they are stored.

| Field | Rule |
|---|---|
| `firstName`, `lastName` | Required, at most 50 characters. Must start with a letter; may contain letters (any language), spaces, `'`, `.` and `-`. |
| `email` | Required, at most 100 characters, `local@domain.tld` with no spaces. Unique (case-insensitive). |
| `department` | Required, at most 50 characters. Must start with a letter or digit; may contain letters, digits, spaces, `&`, `/` and `-`. |
| `page` | 1 or more (default 1). |
| `pageSize` | 1–100 (default 50). |

## Testing

### Automated tests

From the repository root:

```bash
dotnet test
```

There are 73 test cases, and all of them pass. They cover:

- invalid payloads for POST and PUT
- malformed JSON
- real-world names that must be accepted
- missing, zero, negative, non-numeric and overflowing IDs
- email uniqueness rules
- 50 concurrent duplicate POSTs
- 1,000 parallel creates
- paging bounds and overflow
- a simulated database failure
- the middleware ([`MiddlewareTests.cs`](UserManagementAPI.Tests/MiddlewareTests.cs)): missing, invalid and wrong-scheme tokens; every HTTP method protected; the same error shape for 400/401/404/409/500; and log content checked with a capturing logger, including a check that tokens are never logged

### Manual tests

- **Postman:** import [`UserManagementAPI.postman_collection.json`](UserManagementAPI/UserManagementAPI.postman_collection.json) and run it with the Collection Runner. The collection sends the development token automatically, and the 401 requests override it. The 19 requests each check their status code and, where it matters, the response body. The "Create user" request saves the new ID for the PUT and DELETE requests that follow.
- **VS Code / Visual Studio:** open [`UserManagementAPI.http`](UserManagementAPI/UserManagementAPI.http) and click "Send Request" above each block. The token is set once in the `@token` variable.

## Phase 1: How AI assistance built the API

1. **Scaffolding.** It set up the template project for the domain: it removed the `WeatherForecast` sample, kept the template's OpenAPI wiring in `Program.cs` and registered the user service with dependency injection.
2. **Endpoint generation.** It generated all five CRUD actions, following REST conventions: `CreatedAtAction` returns 201 with a `Location` header, DELETE returns 204, and the `{id:int}` route constraint rejects non-numeric IDs.
3. **Separation of concerns.** It moved data access behind `IUserService` instead of keeping a static list inside the controller. The in-memory store can later be swapped for an EF Core/database implementation without changing the controller.
4. **Separate request model.** POST and PUT take a `UserRequest` that has no `Id` or timestamp fields, so clients cannot overwrite server-controlled values (over-posting).
5. **Basic validation and business rules.** It added DataAnnotations and duplicate-email detection (409). Phase 2 found these were incomplete; see below.
6. **Test generation.** It produced the `.http` file and the Postman collection with assertions.

## Phase 2: Debugging

### How the bugs were found

The AI assistant reviewed each file against the reported symptoms. It also asked what could go wrong under concurrent use, because the service is a singleton shared by all requests. It then wrote probe requests against the running phase-1 API to confirm each suspicion before changing any code.

### Bugs found and fixed

| # | Bug | How it was confirmed | Fix |
|---|---|---|---|
| 1 | **Weak email validation.** `[EmailAddress]` only checks for a single `@`, so `a@b` and `x y@z.com` were accepted. | Probe: both returned 201. | Regex that requires no whitespace and a dotted domain (`Models/UserRequest.cs`). |
| 2 | **No content rules on names.** `<script>`, `123` and `!!!` were accepted as names and department. | Probe: returned 201. | Unicode-aware name and department patterns. They still accept real names such as `O'Brien`, `José` and `Mary-Jane`. |
| 3 | **Race on duplicate email.** The controller called `EmailExists`, then `Create`. Two simultaneous POSTs could both pass the check and create duplicates. | Code review (check-then-act across two calls). It did not reproduce in a 20-request burst; the race window is small but real. | The check and the write now happen together under one lock inside the service. Create and Update return a status (`Success` / `NotFound` / `DuplicateEmail`) instead of the controller checking first. Test: 50 concurrent duplicate POSTs give exactly one 201. |
| 4 | **PUT could return `200 OK` with a `null` body.** If a DELETE landed between the controller's `GetById` check and `Update`, `Update` returned `null` and the controller called `Ok(null)`. | Code review. | Fixed by the same atomic `Update`, which now returns `NotFound`. |
| 5 | **Unhandled exceptions.** Nothing caught unexpected exceptions, so a failure in the data layer reached the client unformatted (or as a developer exception page in Development). | Test: a service that throws on every call, swapped in via DI. | `ErrorHandlingMiddleware` wraps the pipeline in try/catch. It logs the exception and returns a 500 problem-details body with a `traceId`, without exposing the exception message. Client cancellations are logged, not treated as errors. |
| 6 | **Empty 404 for bad IDs.** `/api/users/abc` returned a 404 with no body. | Probe. | `AddProblemDetails()` + `UseStatusCodePages()` give body-less errors a problem-details body. |
| 7 | **Inconsistent error formats.** 400s used problem details, while 404 and 409 used `{ "message": ... }`. | Code review. | All errors now use `Problem(...)`. |
| 8 | **`[Produces("application/json")]` overrode error content types.** 404 and 409 were sent as `application/json` instead of `application/problem+json`. | Found by the new integration tests. | Removed the class-level attribute. |
| 9 | **Trimming never happened for padded input.** The validation regexes rejected `"  carol@techhive.com  "` before the service could trim it. | Found by the new integration tests. | The patterns now allow surrounding whitespace; whitespace-only values are still rejected by `[Required]`. |
| 10 | **`GET /api/users` performance.** It returned every user (unbounded response) and ran `OrderBy` on every call (O(n log n)). Duplicate-email checks scanned every user (O(n)). | Code review. | Paging with `page` / `pageSize` (max 100) and an `X-Total-Count` header. A `SortedDictionary` keeps users in ID order, so no per-request sort is needed. A case-insensitive email index makes the duplicate check O(1). |
| 11 | **Integer overflow in paging.** `(page - 1) * pageSize` overflows `int` for large `page` values, which would make `Skip` start at a wrong or negative offset. | Found while adding paging. | Computed as `long`; a page past the end returns an empty list. Test: `page=2147483647` returns 200 with an empty list. |
| 12 | **Mutable shared objects.** `User` was a mutable class handed out from the shared store. | Code review. | `User` is now an immutable `record`; updates replace the stored instance with `with { ... }`. |

### How AI assistance helped

- **Finding bugs beyond the obvious symptoms.** The reports mentioned validation, missing users and crashes. The review also found the concurrency bugs (#3, #4) by reasoning about the singleton lifetime. Black-box testing rarely catches these, as the 20-request burst showed.
- **Probing before fixing.** Each suspected bug was reproduced with a concrete request first, so fixes targeted confirmed problems.
- **Choosing structural fixes over patches.** Instead of adding more checks to the controller, it moved the check-and-write into the service. That removed a whole class of race conditions rather than one instance.
- **Using the framework.** It used `AddProblemDetails`, `UseStatusCodePages`, `IProblemDetailsService` and `[ApiController]` parameter validation instead of writing those by hand.
- **Writing tests that caught its own mistakes.** The integration suite found two bugs (#8, #9) in the first round of fixes. Swapping in a throwing service through DI made it possible to test the crash path without adding test-only endpoints to production code.

## Phase 3: Middleware

### Pipeline order

```text
request ─► ErrorHandling ─► StatusCodePages ─► HttpsRedirection ─► TokenAuthentication ─► RequestLogging ─► routing / controllers
```

| # | Middleware | Why it sits here |
|---|---|---|
| 1 | `ErrorHandlingMiddleware` | Outermost, so its try/catch covers every later component, including the other two middlewares. |
| 2 | `TokenAuthenticationMiddleware` | Rejects unauthenticated requests before routing, model binding or controller work is spent on them. This is the cheapest place to stop them. |
| 3 | `RequestLoggingMiddleware` | Runs after authentication, so every log line can name the client that made the call. |

**Trade-off of putting logging last.** Requests rejected with 401 never reach the logging middleware. For auditing, those are exactly the requests that matter, so `TokenAuthenticationMiddleware` logs each rejection itself (method, path, remote IP, reason). Together, the two produce exactly one audit line per request.

### Logging middleware

- **Format:** one structured line per request, for example `HTTP GET /api/users/1 responded 200 in 3.2 ms (client: hr-portal)`.
- **Log level follows the outcome:** 2xx/3xx → Information, 4xx → Warning, 5xx → Error.
- **Exceptions:** when a later component throws, the middleware logs the 500 the client will receive, then rethrows so the error middleware can respond.
- **Performance:** it uses the `[LoggerMessage]` source generator, so a disabled log level costs no string formatting or allocations.
- **Privacy:** request and response bodies and headers are not logged, because they carry personal data and tokens. Tests check that tokens never appear in any log.

### Error-handling middleware

- **Catch:** a try/catch around the whole pipeline catches every unhandled exception.
- **Logging:** the exception and its stack trace go to the server log.
- **Response:** the client receives `500 { "error": "Internal server error.", "traceId": ... }` in the standard problem-details shape.
- **Shared format:** a single `CustomizeProblemDetails` hook adds the `error` field to every error the API produces (400, 401, 404, 409 and 500), so clients parse one format.
- **Edge cases:** a client that disconnects mid-request is logged as cancelled, not as a failure. If the response has already started, the exception is rethrown, because the status code can no longer change.

### Authentication middleware

- **Header check:** it accepts `Authorization: Bearer <token>`. The scheme is case-insensitive, and surrounding whitespace is ignored.
- **Rejections:** a missing token, the wrong scheme and an unknown token each get a 401 with a specific `detail` message. The response includes a `WWW-Authenticate: Bearer` challenge, plus `error="invalid_token"` for an unknown token (RFC 6750).
- **Token comparison:** tokens are compared as SHA-256 hashes with `CryptographicOperations.FixedTimeEquals`, and every configured token is checked, with no early exit. Response timing therefore reveals nothing about how close a guess was.
- **Client identity:** on success, the client name is attached to `HttpContext.User`, and the logging middleware reports it.
- **Fails closed:** with no tokens configured, every request is rejected and a warning is logged at startup. Tokens shorter than 32 characters stop the app from starting.
- **Scope:** only `/api/*` is protected. The OpenAPI document (Development only) stays readable.

### Verified behaviour

The server log from a live run against the development server:

```text
warn: TokenAuthenticationMiddleware  Rejected GET /api/users from ::1 with 401: Missing bearer token.
warn: TokenAuthenticationMiddleware  Rejected GET /api/users from ::1 with 401: Invalid bearer token.
info: RequestLoggingMiddleware       HTTP GET /api/users responded 200 in 27.3 ms (client: hr-portal)
warn: RequestLoggingMiddleware       HTTP GET /api/users/99 responded 404 in 7.1 ms (client: hr-portal)
fail: RequestLoggingMiddleware       HTTP GET /api/debug/throw responded 500 in 0.4 ms (client: hr-portal)
fail: ErrorHandlingMiddleware        Unhandled exception for GET /api/debug/throw
      System.InvalidOperationException: Simulated failure for testing.
```

### How AI assistance helped

- **Generated all three middlewares,** including the details a first draft usually misses: constant-time token comparison, the `WWW-Authenticate` challenge header, logging the real 500 status when an exception passes through, and not logging secrets.
- **Spotted a conflict in the requirements.** The required order (logging last) would leave failed authentication attempts unaudited. It was resolved by having the authentication middleware audit its own rejections, so the specified order is kept.
- **Unified error responses** with one `CustomizeProblemDetails` hook instead of formatting errors separately in each middleware and controller.
- **Wrote tests that check log output,** not just HTTP responses. One of them caught a bug: `HttpClient` trims `"Bearer "` to `"Bearer"`, so a header with no token was reported as "wrong scheme". The fix was to parse the scheme and the token separately.
