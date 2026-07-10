using Api.Security;
using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Api.Extensions;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── Health ────────────────────────────────────────────────────────────
        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "v1" }));

         //─── Public marketplace(cached) ───────────────────────────────────────
        // Phase 5: suporta filtros ?verificationStatus=verified&minRating=4.5 além dos filtros base
        app.MapGet("/professionals", async (
            HttpContext ctx,
            string? zoneId, string? serviceId,
            string? verificationStatus, double? minRating,
            string? professionalId, int? page, int? pageSize,
            IProfessionalReadRepository repo, IMemoryCache cache, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("HomeEndpoints");
            var effPage = ClampPage(page);
            var effPageSize = ClampPageSize(pageSize);
            var hasExtraFilters = !string.IsNullOrWhiteSpace(verificationStatus) || minRating.HasValue;
            var cacheKey = hasExtraFilters
                ? $"professionals:{zoneId ?? "*"}:{serviceId ?? "*"}:vs={verificationStatus ?? "*"}:mr={minRating}:pro={professionalId ?? "*"}:pg={effPage}:ps={effPageSize}"
                : $"professionals:{zoneId ?? "*"}:{serviceId ?? "*"}:pro={professionalId ?? "*"}:pg={effPage}:ps={effPageSize}";
            var result = await GetOrCreateCachedAsync(cache, cacheKey, TimeSpan.FromSeconds(45), ShouldBypassCache(ctx.Request),
                () => hasExtraFilters
                    ? repo.GetProfessionalsFilteredAsync(zoneId, serviceId, verificationStatus, minRating, professionalId, effPage, effPageSize, ct)
                    : repo.GetProfessionalsAsync(zoneId, serviceId, professionalId, effPage, effPageSize, ct),
                logger, ct);
            SetPaginationHeaders(ctx.Response, result.TotalCount, effPage, effPageSize);
            return Results.Ok(new
            {
                items      = result.Items,
                total      = result.TotalCount,
                page       = effPage,
                pageSize   = effPageSize,
                totalPages = (int)Math.Ceiling(result.TotalCount / (double)effPageSize),
            });
        });

        app.MapGet("/zones", async (HttpRequest req, IProfessionalReadRepository repo, IMemoryCache cache, CancellationToken ct) =>
        {
            var zones = await GetOrCreateCachedAsync(cache, "zones:active", TimeSpan.FromMinutes(10), ShouldBypassCache(req),
                () => repo.GetZonesAsync(ct), logger: null, ct);
            return Results.Ok(zones);
        });

        app.MapGet("/services", async (HttpRequest req, string? categoryId, IProfessionalReadRepository repo, IMemoryCache cache, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                var filtered = await GetOrCreateCachedAsync(cache, $"services:cat:{categoryId}", TimeSpan.FromMinutes(10), ShouldBypassCache(req),
                    () => repo.GetServicesByCategoryAsync(categoryId, ct), logger: null, ct);
                return Results.Ok(filtered);
            }
            var services = await GetOrCreateCachedAsync(cache, "services:all", TimeSpan.FromMinutes(10), ShouldBypassCache(req),
                () => repo.GetServicesAsync(ct), logger: null, ct);
            return Results.Ok(services);
        });

        app.MapGet("/tiers", async (HttpRequest req, IServiceCatalogRepository catalog, IMemoryCache cache, CancellationToken ct) =>
        {
            var tiers = await GetOrCreateCachedAsync(cache, "tiers:all", TimeSpan.FromHours(1), ShouldBypassCache(req),
                async () => (await catalog.GetTiersAsync(ct)).Select(t => new TierDto(
                    t.Id, t.Name, t.Code, t.AllowBookingDirect, t.RequiresProposal, t.RequiresChat,
                    t.AllowedPriceFormats)).ToList(),
                logger: null, ct);
            return Results.Ok(tiers);
        });

        app.MapGet("/categories", async (HttpRequest req, IServiceCatalogRepository catalog, IMemoryCache cache, CancellationToken ct) =>
        {
            var categories = await GetOrCreateCachedAsync(cache, "categories:all", TimeSpan.FromHours(1), ShouldBypassCache(req),
                async () => (await catalog.GetCategoriesAsync(ct)).Select(c => new CategoryDto(c.Id, c.Name, c.Icon)).ToList(),
                logger: null, ct);
            return Results.Ok(categories);
        });

        app.MapGet("/bootstrap", async (HttpRequest req, IProfessionalReadRepository repo, IServiceCatalogRepository catalog, IMemoryCache cache, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("HomeEndpoints");
            var bootstrap = await GetOrCreateCachedAsync(cache, "home:bootstrap", TimeSpan.FromSeconds(45), ShouldBypassCache(req),
                async () =>
                {
                    // IProfessionalReadRepository e IServiceCatalogRepository compartilham o mesmo AppDbContext scoped.
                    // Executar queries em paralelo aqui dispara concorrência no DbContext (não thread-safe).
                    var professionals = await repo.GetProfessionalsAsync(null, null, null, 1, DefaultPageSize, ct);
                    var zones = await repo.GetZonesAsync(ct);
                    var services = await repo.GetServicesAsync(ct);
                    var categoriesRaw = await catalog.GetCategoriesAsync(ct);
                    var tiersRaw = await catalog.GetTiersAsync(ct);

                    var categories = categoriesRaw.Select(c => new CategoryDto(c.Id, c.Name, c.Icon)).ToList();
                    var tiers = tiersRaw.Select(t => new TierDto(
                        t.Id, t.Name, t.Code, t.AllowBookingDirect, t.RequiresProposal, t.RequiresChat,
                        t.AllowedPriceFormats)).ToList();
                    var hasMore = professionals.TotalCount > DefaultPageSize;
                    return new HomeBootstrapDto(professionals.Items, zones, services, categories, tiers,
                        ProfessionalsTotal: professionals.TotalCount, ProfessionalsHasMore: hasMore);
                }, logger, ct);
            return Results.Ok(bootstrap);
        });

        app.MapGet("/home/bootstrap", (HttpContext ctx) =>
            Results.Redirect($"/bootstrap{ctx.Request.QueryString}"));

        // ─── Auth ──────────────────────────────────────────────────────────────
        app.MapPost("/auth", async (
            LoginRequest body,
            IAuthRepository db,
            AppDbContext ctx,
            IConfiguration config,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email))
                return Results.Json(new { error = "email é obrigatório." }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.Senha))
                return Results.Json(new { error = "senha é obrigatória." }, statusCode: 400);

            var userObj = await db.LoginAsync(body.Email, body.Senha, ct);
            if (userObj is null)
                return Results.Json(new { error = "Credenciais inválidas" }, statusCode: 401);

            // Parse once — reused for JWT generation, professionalId lookup, and response shape.
            var userJson = JsonSerializer.Serialize(userObj);
            using var userDoc = JsonDocument.Parse(userJson);
            var root = userDoc.RootElement;
            var userId = root.TryGetProperty("id",    out var idEl)    ? idEl.GetString()    ?? "" : "";
            var email  = root.TryGetProperty("email", out var emailEl) ? emailEl.GetString() ?? "" : "";
            var role   = root.TryGetProperty("role",  out var roleEl)  ? roleEl.GetString()  ?? "" : "";

            var professionalId = await ResolveProfessionalIdAsync(ctx, role, userId, ct);

            // Geração de JWT na camada de API (camada Infrastructure não tem dep. de JWT)
            string? token = null;
            var jwtSecret = config["JWT_SECRET"];
            if (!string.IsNullOrWhiteSpace(jwtSecret))
            {
                var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
                var claims = new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, userId),
                    new Claim(JwtRegisteredClaimNames.Email, email),
                    new Claim("role", role),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                };
                var jwtToken = new JwtSecurityToken(
                    issuer: "jobeasy",
                    audience: "jobeasy",
                    claims: claims,
                    notBefore: DateTime.UtcNow,
                    expires: DateTime.UtcNow.AddDays(7),
                    signingCredentials: creds);
                token = new JwtSecurityTokenHandler().WriteToken(jwtToken);
            }

            object? r_defaultAddress = null;
            if (root.TryGetProperty("defaultAddress", out var r_addrEl) && r_addrEl.ValueKind == JsonValueKind.Object)
            {
                r_defaultAddress = new
                {
                    zipCode      = r_addrEl.TryGetProperty("zipCode",      out var rp) ? rp.GetString() : null,
                    street       = r_addrEl.TryGetProperty("street",       out rp)     ? rp.GetString() : null,
                    number       = r_addrEl.TryGetProperty("number",       out rp)     ? rp.GetString() : null,
                    neighborhood = r_addrEl.TryGetProperty("neighborhood", out rp)     ? rp.GetString() : null,
                    city         = r_addrEl.TryGetProperty("city",         out rp)     ? rp.GetString() : null,
                    state        = r_addrEl.TryGetProperty("state",        out rp)     ? rp.GetString() : null,
                    complement   = r_addrEl.TryGetProperty("complement",   out rp)     ? rp.GetString() : null,
                    reference    = r_addrEl.TryGetProperty("reference",    out rp)     ? rp.GetString() : null,
                };
            }

            return Results.Ok(new
            {
                token,
                user = new
                {
                    id             = userId.Length > 0 ? userId : (string?)null,
                    name           = root.TryGetProperty("name",        out var r_name)  ? r_name.GetString()  : null,
                    email          = root.TryGetProperty("email",       out var r_email) ? r_email.GetString() : null,
                    role           = root.TryGetProperty("role",        out var r_role)  ? r_role.GetString()  : null,
                    phone          = root.TryGetProperty("phone",       out var r_phone) ? r_phone.GetString() : null,
                    zoneId         = root.TryGetProperty("zoneId",      out var r_zone)  ? r_zone.GetString()  : null,
                    mpConnected    = root.TryGetProperty("mpConnected", out var r_mp)    && r_mp.GetBoolean(),
                    professionalId,
                    defaultAddress = r_defaultAddress,
                }
            });
        }).RequireRateLimiting("auth");

        // ─── Social Auth ────────────────────────────────────────────────────────
        app.MapPost("/auth/google", async (
            GoogleLoginRequest body,
            ISocialAuthService socialAuth,
            IUserRepository repo,
            AppDbContext ctx,
            IConfiguration config,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.IdToken))
                return Results.Json(new { error = "idToken é obrigatório" }, statusCode: 400);

            SocialUserInfo info;
            try
            {
                info = await socialAuth.ValidateGoogleTokenAsync(body.IdToken, ct);
            }
            catch (SocialAuthException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }

            var (userObj, isNewUser) = await repo.FindOrCreateSocialUserAsync(info.Provider, info.ProviderUserId, info.Email, info.Name, ct);

            var token = GenerateJwt(config, userObj);

            var socialJson = JsonSerializer.Serialize(userObj);
            using var socialDoc = JsonDocument.Parse(socialJson);
            var sRoot = socialDoc.RootElement;
            var sUserId = sRoot.TryGetProperty("id",   out var sIdEl)   ? sIdEl.GetString()   ?? "" : "";
            var sRole   = sRoot.TryGetProperty("role", out var sRoleEl) ? sRoleEl.GetString() ?? "" : "";
            var sProfessionalId = await ResolveProfessionalIdAsync(ctx, sRole, sUserId, ct);

            object? sgDefaultAddress = null;
            if (sRoot.TryGetProperty("defaultAddress", out var sgAddrEl) && sgAddrEl.ValueKind == JsonValueKind.Object)
            {
                sgDefaultAddress = new
                {
                    zipCode      = sgAddrEl.TryGetProperty("zipCode",      out var p) ? p.GetString() : null,
                    street       = sgAddrEl.TryGetProperty("street",       out p)     ? p.GetString() : null,
                    number       = sgAddrEl.TryGetProperty("number",       out p)     ? p.GetString() : null,
                    neighborhood = sgAddrEl.TryGetProperty("neighborhood", out p)     ? p.GetString() : null,
                    city         = sgAddrEl.TryGetProperty("city",         out p)     ? p.GetString() : null,
                    state        = sgAddrEl.TryGetProperty("state",        out p)     ? p.GetString() : null,
                    complement   = sgAddrEl.TryGetProperty("complement",   out p)     ? p.GetString() : null,
                    reference    = sgAddrEl.TryGetProperty("reference",    out p)     ? p.GetString() : null,
                };
            }

            return Results.Ok(new
            {
                token,
                isNewUser,
                user = new
                {
                    id             = sUserId.Length > 0 ? sUserId : (string?)null,
                    name           = sRoot.TryGetProperty("name",        out var sg_name)  ? sg_name.GetString()  : null,
                    email          = sRoot.TryGetProperty("email",       out var sg_email) ? sg_email.GetString() : null,
                    role           = sRoot.TryGetProperty("role",        out var sg_role)  ? sg_role.GetString()  : null,
                    phone          = sRoot.TryGetProperty("phone",       out var sg_phone) ? sg_phone.GetString() : null,
                    zoneId         = sRoot.TryGetProperty("zoneId",      out var sg_zone)  ? sg_zone.GetString()  : null,
                    mpConnected    = sRoot.TryGetProperty("mpConnected", out var sg_mp)    && sg_mp.GetBoolean(),
                    professionalId = sProfessionalId,
                    defaultAddress = sgDefaultAddress,
                }
            });
        }).RequireRateLimiting("auth");

        app.MapPost("/auth/facebook", async (
            FacebookLoginRequest body,
            ISocialAuthService socialAuth,
            IUserRepository repo,
            AppDbContext ctx,
            IConfiguration config,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.AccessToken))
                return Results.Json(new { error = "accessToken é obrigatório" }, statusCode: 400);

            SocialUserInfo info;
            try
            {
                info = await socialAuth.ValidateFacebookTokenAsync(body.AccessToken, ct);
            }
            catch (SocialAuthException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }

            var (userObj, isNewUser) = await repo.FindOrCreateSocialUserAsync(info.Provider, info.ProviderUserId, info.Email, info.Name, ct);

            var token = GenerateJwt(config, userObj);

            var fbJson = JsonSerializer.Serialize(userObj);
            using var fbDoc = JsonDocument.Parse(fbJson);
            var fRoot = fbDoc.RootElement;
            var fUserId = fRoot.TryGetProperty("id",   out var fIdEl)   ? fIdEl.GetString()   ?? "" : "";
            var fRole   = fRoot.TryGetProperty("role", out var fRoleEl) ? fRoleEl.GetString() ?? "" : "";
            var fProfessionalId = await ResolveProfessionalIdAsync(ctx, fRole, fUserId, ct);

            object? fbDefaultAddress = null;
            if (fRoot.TryGetProperty("defaultAddress", out var fbAddrEl) && fbAddrEl.ValueKind == JsonValueKind.Object)
            {
                fbDefaultAddress = new
                {
                    zipCode      = fbAddrEl.TryGetProperty("zipCode",      out var p) ? p.GetString() : null,
                    street       = fbAddrEl.TryGetProperty("street",       out p)     ? p.GetString() : null,
                    number       = fbAddrEl.TryGetProperty("number",       out p)     ? p.GetString() : null,
                    neighborhood = fbAddrEl.TryGetProperty("neighborhood", out p)     ? p.GetString() : null,
                    city         = fbAddrEl.TryGetProperty("city",         out p)     ? p.GetString() : null,
                    state        = fbAddrEl.TryGetProperty("state",        out p)     ? p.GetString() : null,
                    complement   = fbAddrEl.TryGetProperty("complement",   out p)     ? p.GetString() : null,
                    reference    = fbAddrEl.TryGetProperty("reference",    out p)     ? p.GetString() : null,
                };
            }

            return Results.Ok(new
            {
                token,
                isNewUser,
                user = new
                {
                    id             = fUserId.Length > 0 ? fUserId : (string?)null,
                    name           = fRoot.TryGetProperty("name",        out var fb_name)  ? fb_name.GetString()  : null,
                    email          = fRoot.TryGetProperty("email",       out var fb_email) ? fb_email.GetString() : null,
                    role           = fRoot.TryGetProperty("role",        out var fb_role)  ? fb_role.GetString()  : null,
                    phone          = fRoot.TryGetProperty("phone",       out var fb_phone) ? fb_phone.GetString() : null,
                    zoneId         = fRoot.TryGetProperty("zoneId",      out var fb_zone)  ? fb_zone.GetString()  : null,
                    mpConnected    = fRoot.TryGetProperty("mpConnected", out var fb_mp)    && fb_mp.GetBoolean(),
                    professionalId = fProfessionalId,
                    defaultAddress = fbDefaultAddress,
                }
            });
        }).RequireRateLimiting("auth");

        // ─── Users ─────────────────────────────────────────────────────────────
        app.MapPost("/users", async (CreateUserRequest body, HttpContext context, IUserRepository repo, CancellationToken ct) =>
        {
            var name = body.Name?.Trim() ?? "";
            var email = body.Email?.Trim() ?? "";
            var role = body.Role?.Trim() ?? "";
            var senha = body.Senha ?? "";

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(role) || string.IsNullOrEmpty(senha))
                return Results.Json(new { error = "name, email, role e senha são obrigatórios" }, statusCode: 400);
            if (role != "cliente" && role != "profissional" && role != "admin")
                return Results.Json(new { error = "role inválido" }, statusCode: 400);
            if (role == "admin" && !AuthorizationHelpers.IsAdmin(context))
                return Results.Json(new { error = "Não é possível se cadastrar como admin." }, statusCode: 403);
            if (role == "cliente" && string.IsNullOrWhiteSpace(body.ZoneId))
                return Results.Json(new { error = "zoneId é obrigatório para clientes" }, statusCode: 400);
            if (await repo.EmailExistsAsync(email, ct))
                return Results.Json(new { error = "Já existe um usuário com este email" }, statusCode: 400);
            if (!string.IsNullOrWhiteSpace(body.ZoneId) && !await repo.ZoneExistsAndActiveAsync(body.ZoneId, ct))
                return Results.Json(new { error = "zoneId inválido (zona inexistente ou inativa)" }, statusCode: 400);

            // Validate default address if provided
            if (body.DefaultAddress is not null)
            {
                var (addrValid, addrError) = Application.Services.AddressResolver.Validate(body.DefaultAddress);
                if (!addrValid)
                    return Results.Json(new { error = addrError }, statusCode: 400);
            }

            var hashed = BCrypt.Net.BCrypt.HashPassword(senha, workFactor: 10);
            var user = await repo.CreateAsync(name, email, body.Phone?.Trim(), role, hashed, body.ZoneId?.Trim(), ct, body.DefaultAddress);
            return Results.Json(user, statusCode: 201);
        });

        app.MapGet("/users/{id}", async (
            string id,
            IUserRepository repo,
            CancellationToken ct) =>
        {
            var user = await repo.GetByIdAsync(id, ct);
            return user is null
                ? Results.Json(new { error = "Usuário não encontrado" }, statusCode: 404)
                : Results.Ok(user);
        });

        // ─── User default address ───────────────────────────────────────────────
        app.MapGet("/users/{id}/default-address", async (
            string id,
            IUserRepository repo,
            CancellationToken ct) =>
        {
            var address = await repo.GetDefaultAddressAsync(id, ct);
            return address is null
                ? Results.NotFound(new { error = "Cliente não possui endereço padrão cadastrado" })
                : Results.Ok(address);
        });

        app.MapPut("/users/{id}/default-address", async (
            string id,
            Application.DTOs.UpdateDefaultAddressRequest body,
            HttpContext context,
            IUserRepository repo,
            CancellationToken ct) =>
        {
            if (AuthorizationHelpers.RequireSelfOrAdmin(context, id) is { } authError)
                return authError;

            if (body.Address is null)
                return Results.Json(new { error = "address é obrigatório" }, statusCode: 400);

            var (valid, error) = Application.Services.AddressResolver.Validate(body.Address);
            if (!valid)
                return Results.Json(new { error }, statusCode: 400);

            await repo.UpdateDefaultAddressAsync(id, body.Address, ct);
            return Results.Ok(new { ok = true, defaultAddress = body.Address });
        });

        app.MapPut("/users/{id}", async (
            string id,
            Application.DTOs.UpdateUserRequest body,
            IUserRepository repo,
            CancellationToken ct) =>
        {
            var user = await repo.GetByIdAsync(id, ct);
            if (user is null)
                return Results.Json(new { error = "Usuário não encontrado" }, statusCode: 404);

            var trimmedZoneId = body.ZoneId?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedZoneId) && !await repo.ZoneExistsAndActiveAsync(trimmedZoneId, ct))
                return Results.Json(new { error = "zoneId inválido (zona inexistente ou inativa)" }, statusCode: 400);

            await repo.UpdateUserAsync(id, body.Name?.Trim(), body.Phone?.Trim(), trimmedZoneId, ct);

            if (body.DefaultAddress is not null)
            {
                var (valid, addressError) = Application.Services.AddressResolver.Validate(body.DefaultAddress);
                if (!valid)
                    return Results.Json(new { error = addressError }, statusCode: 400);

                await repo.UpdateDefaultAddressAsync(id, body.DefaultAddress, ct);
            }

            var updatedUser = await repo.GetByIdAsync(id, ct);
            return Results.Ok(updatedUser);
        });

        // ─── GET /users/{id}/addresses ──────────────────────────────────────────
        app.MapGet("/users/{id}/addresses", async (
            string id, HttpRequest req, IUserRepository repo, CancellationToken ct) =>
        {
            if (RequireSelf(req.HttpContext, id, out _) is { } err) return err;
            var addresses = await repo.GetAddressesAsync(id, ct);
            return Results.Ok(addresses);
        });

        // ─── POST /users/{id}/addresses ─────────────────────────────────────────
        app.MapPost("/users/{id}/addresses", async (
            string id, Application.DTOs.CreateUserAddressRequest body,
            HttpRequest req, IUserRepository repo, CancellationToken ct) =>
        {
            if (RequireSelf(req.HttpContext, id, out _) is { } err) return err;

            var addressToValidate = new Application.DTOs.AddressDto(
                body.ZipCode, body.Street, body.Number,
                body.Neighborhood, body.City, body.State,
                body.Complement, body.Reference);

            var (valid, error) = Application.Services.AddressResolver.Validate(addressToValidate);
            if (!valid)
                return Results.Json(new { error }, statusCode: 400);

            var created = await repo.CreateAddressAsync(id, body, ct);
            return Results.Json(created, statusCode: 201);
        });

        // ─── POST /users/{id}/addresses/{addrId}/use ────────────────────────────
        app.MapPost("/users/{id}/addresses/{addrId}/use", async (
            string id, string addrId,
            HttpRequest req, IUserRepository repo, CancellationToken ct) =>
        {
            if (RequireSelf(req.HttpContext, id, out _) is { } err) return err;

            await repo.MarkAddressAsUsedAsync(addrId, id, ct);
            return Results.Ok(new { ok = true });
        });

        // ─── DELETE /users/{id}/addresses/{addrId} ──────────────────────────────
        app.MapDelete("/users/{id}/addresses/{addrId}", async (
            string id, string addrId,
            HttpRequest req, IUserRepository repo, CancellationToken ct) =>
        {
            if (RequireSelf(req.HttpContext, id, out _) is { } err) return err;

            var deleted = await repo.DeleteAddressAsync(addrId, id, ct);
            return deleted
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new { error = "Endereço não encontrado" });
        });

        // ─── Professionals ─────────────────────────────────────────────────────
        //app.MapGet("/professionals", async (
        //    HttpContext ctx, IMemoryCache cache, string? serviceId, string? zoneId,
        //    string? excludeProfessionalId, string? professionalId, bool? filterZones,
        //    IProfessionalRepository repo, CancellationToken ct) =>
        //    await GetOrSetCachedListAsync(ctx, cache, "professionals-cards", TimeSpan.FromSeconds(60),
        //        async token => await repo.GetProfessionalCardsAsync(serviceId, zoneId, excludeProfessionalId, professionalId, filterZones == true, token), ct));

        app.MapPost("/professionals", async (CreateProfessionalRequest body, IProfessionalDetailRepository repo, CancellationToken ct) =>
        {
            var userId = body.UserId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Json(new { error = "userId é obrigatório" }, statusCode: 400);

            var zoneIds = (body.Zones ?? [])
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Select(z => z.Trim())
                .Distinct()
                .ToArray();

            if (!await repo.UserExistsAsync(userId, ct))
                return Results.Json(new { error = "Usuário não encontrado." }, statusCode: 400);

            if (await repo.ProfessionalExistsByUserIdAsync(userId, ct))
                return Results.Json(new { error = "Usuário já possui cadastro de profissional." }, statusCode: 400);

            if (!await repo.ZonesExistAndActiveAsync(zoneIds, ct))
                return Results.Json(new { error = "Uma ou mais zonas são inválidas ou estão inativas." }, statusCode: 400);

            var created = await repo.CreateAsync(userId, body.Bio, body.Active ?? true, zoneIds, ct);
            return Results.Json(created, statusCode: 201);
        });

        app.MapGet("/professionals/zones", async (string? professionalId, IProfessionalDetailRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetZonesAsync(professionalId, ct));
        });

        app.MapPut("/professionals/zones", async (
            UpdateProfessionalZonesRequest body,
            HttpContext context,
            IProfessionalDetailRepository repo,
            AppDbContext dbCtx,
            CancellationToken ct) =>
        {
            var (resolvedId, authError) = await AuthorizationHelpers.ResolveProfessionalIdAsync(
                context, body.ProfessionalId, dbCtx, ct);
            if (authError is not null)
                return authError;

            try
            {
                var result = await repo.UpdateZonesAsync(resolvedId!, body.Zones ?? [], ct);
                return result is null ? Results.NotFound(new { error = "Profissional não encontrado" }) : Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        app.MapGet("/professionals/{id}", async (string id, IProfessionalDetailRepository repo, CancellationToken ct) =>
        {
            var pro = await repo.GetByIdAsync(id, ct);
            return pro is null ? Results.NotFound(new { error = "Profissional não encontrado." }) : Results.Ok(pro);
        });

        app.MapPut("/professionals/{id}", async (
            string id,
            UpdateProfessionalRequest body,
            HttpContext context,
            IProfessionalDetailRepository repo,
            AppDbContext dbCtx,
            CancellationToken ct) =>
        {
            var professional = await dbCtx.Professionals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (professional is null)
                return Results.NotFound(new { error = "Profissional não encontrado." });

            if (!AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            var updated = await repo.UpdateAsync(id, body.Bio, body.Active, body.AvailabilityText, body.AvatarUrl, ct);
            return updated is null ? Results.NotFound(new { error = "Profissional não encontrado." }) : Results.Ok(updated);
        });

        app.MapPost("/upload-avatar", async (
            HttpRequest req,
            HttpContext context,
            IProfessionalDetailRepository professionalRepo,
            IAvatarStorageRepository avatarStorageRepo,
            AppDbContext dbCtx,
            CancellationToken ct) =>
        {
            const long maxSizeBytes = 5 * 1024 * 1024;
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

            if (!req.HasFormContentType)
                return Results.Json(new { error = "Content-Type deve ser multipart/form-data." }, statusCode: 400);

            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            var professionalId = (form["professionalId"].FirstOrDefault() ?? string.Empty).Trim();

            if (file is null || file.Length == 0)
                return Results.Json(new { error = "Arquivo não enviado." }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório." }, statusCode: 400);

            var professional = await dbCtx.Professionals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == professionalId, ct);
            if (professional is null)
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 404);

            if (!AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            if (!allowedTypes.Contains(contentType))
                return Results.Json(new { error = "Formato inválido. Use JPG, PNG ou WEBP." }, statusCode: 400);
            if (file.Length > maxSizeBytes)
                return Results.Json(new { error = "Arquivo excede 5MB." }, statusCode: 400);

            await using var fileStream = file.OpenReadStream();
            var publicUrl = await avatarStorageRepo.UploadProfessionalAvatarAsync(professionalId, fileStream, contentType, ct);
            if (string.IsNullOrWhiteSpace(publicUrl))
                return Results.Json(new { error = "Falha no upload." }, statusCode: 500);

            var updated = await professionalRepo.UpdateAvatarUrlAsync(professionalId, publicUrl, ct);
            if (!updated)
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 404);

            return Results.Ok(new { ok = true, avatarUrl = publicUrl });
        });

        // ─── Professional Services ──────────────────────────────────────────────
        app.MapGet("/professional-services", async (string? professionalId, string? serviceId, IProfessionalServiceRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAsync(professionalId, serviceId, ct)));

        app.MapPost("/professional-services", async (CreateProfessionalServiceRequest body, IProfessionalServiceRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.ServiceId) || string.IsNullOrWhiteSpace(body.NomeServico))
                return Results.Json(new { error = "professionalId, serviceId e nomeServico são obrigatórios" }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(body.ProfessionalId, ct))
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 400);
            if (!await repo.ServiceExistsAsync(body.ServiceId, ct))
                return Results.Json(new { error = "Serviço (categoria) não encontrado." }, statusCode: 400);

            // ── Nova validação: tipoContratacao ───────────────────────────────────
            if (body.TipoContratacao is not null)
            {
                var validator = new Application.Validation.CreateProfessionalServiceRequestValidator();
                var validationResult = await validator.ValidateAsync(body, ct);
                if (!validationResult.IsValid)
                    return Results.Json(new { error = validationResult.Errors.First().ErrorMessage }, statusCode: 400);
            }
            // ── Compatibilidade retroativa: tierId + contractMode (legado) ─────────
            else if (body.TierId.HasValue || body.ContractMode is not null)
            {
                if (!body.TierId.HasValue)
                    return Results.Json(new { error = "tierId é obrigatório quando contractMode é informado." }, statusCode: 400);
                if (string.IsNullOrWhiteSpace(body.ContractMode))
                    return Results.Json(new { error = "contractMode é obrigatório quando tierId é informado." }, statusCode: 400);

                var validModes = new[] { ContractMode.Booking, ContractMode.Proposal };
                if (!validModes.Contains(body.ContractMode))
                    return Results.Json(new { error = "contractMode inválido. Valores aceitos: booking, proposal." }, statusCode: 400);

                int[] bookingTiers = [1, 4];
                int[] proposalTiers = [2, 3];

                if (!bookingTiers.Concat(proposalTiers).Contains(body.TierId.Value))
                    return Results.Json(new { error = "tierId inválido. Valores aceitos: 1, 2, 3, 4." }, statusCode: 400);

                if (body.ContractMode == ContractMode.Booking && !bookingTiers.Contains(body.TierId.Value))
                    return Results.Json(new { error = "contractMode 'booking' é incompatível com o tierId informado. Tiers permitidos para booking: 1, 4." }, statusCode: 400);

                if (body.ContractMode == ContractMode.Proposal && !proposalTiers.Contains(body.TierId.Value))
                    return Results.Json(new { error = "contractMode 'proposal' é incompatível com o tierId informado. Tiers permitidos para proposal: 2, 3." }, statusCode: 400);

                if (body.ContractMode == ContractMode.Booking && (!body.DurationMinutes.HasValue || body.DurationMinutes.Value <= 0))
                    return Results.Json(new { error = "durationMinutes é obrigatório e deve ser maior que zero para contractMode 'booking'." }, statusCode: 400);

                if (!body.Preco.HasValue || body.Preco.Value <= 0)
                    return Results.Json(new { error = "precoBase é obrigatório." }, statusCode: 400);
            }
            else
            {
                // Sem tipoContratacao e sem tierId/contractMode: precoBase obrigatório
                if (!body.Preco.HasValue || body.Preco.Value <= 0)
                    return Results.Json(new { error = "precoBase é obrigatório." }, statusCode: 400);
            }

            // Derivar tipoContratacao do contractMode legado se não fornecido diretamente
            var tipoContratacao = body.TipoContratacao
                ?? body.ContractMode switch {
                    ContractMode.Booking  => TipoContratacao.ReservaDireta,
                    ContractMode.Proposal => TipoContratacao.Proposta,
                    _                     => null
                };

            // For PROPOSTA: force preco to null regardless of what was sent
            var preco = tipoContratacao == TipoContratacao.Proposta ? null : body.Preco;

            var created = await repo.CreateAsync(body.ProfessionalId, body.ServiceId, body.NomeServico.Trim(), preco, body.Descricao, body.TierId, body.ContractMode, body.DurationMinutes, body.MinLeadTimeMinutes, tipoContratacao, ct);
            return Results.Json(created, statusCode: 201);
        });

        app.MapGet("/professional-services/{id}", async (string id, IProfessionalServiceRepository repo, CancellationToken ct) =>
        {
            var svc = await repo.GetByIdAsync(id, ct);
            return svc is null ? Results.NotFound(new { error = "Serviço não encontrado" }) : Results.Ok(svc);
        });

        app.MapPut("/professional-services/{id}", async (
            string id,
            UpdateProfessionalServiceRequest body,
            HttpContext context,
            IProfessionalServiceRepository repo,
            AppDbContext dbCtx,
            CancellationToken ct) =>
        {
            var authError = await RequireProfessionalServiceOwnerOrAdminAsync(id, context, dbCtx, ct);
            if (authError is not null)
                return authError;

            var updated = await repo.UpdateAsync(id, body.NomeServico, body.Preco, body.Descricao, ct);
            return updated is null ? Results.NotFound(new { error = "Serviço não encontrado" }) : Results.Ok(updated);
        });

        app.MapDelete("/professional-services/{id}", async (
            string id,
            HttpContext context,
            IProfessionalServiceRepository repo,
            AppDbContext dbCtx,
            CancellationToken ct) =>
        {
            var authError = await RequireProfessionalServiceOwnerOrAdminAsync(id, context, dbCtx, ct);
            if (authError is not null)
                return authError;

            var deleted = await repo.DeleteAsync(id, ct);
            return deleted ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "Serviço não encontrado" });
        });

        // ─── Orders ────────────────────────────────────────────────────────────
        // filterZones e active aceitam "true"/"false" e também "1"/"0" para compatibilidade com frontends
        // que enviam valores numéricos em query params (ex: filterZones=1).
        app.MapGet("/orders", async (
            HttpContext ctx, IMemoryCache cache, string? serviceId, string? excludeProfessionalId,
            string? professionalId, string? filterZones, string? active, int? page, int? pageSize,
            IOrderRepository repo, AppDbContext dbCtx, CancellationToken ct) =>
        {
            var effPage = ClampPage(page);
            var effPageSize = ClampPageSize(pageSize);
            var cacheKey = $"orders:svc={serviceId ?? "*"}:excl={excludeProfessionalId ?? "*"}:pro={professionalId ?? "*"}:fz={filterZones ?? "0"}:act={active ?? "0"}:pg={effPage}:ps={effPageSize}";

            var paged = await GetOrCreateCachedAsync(
                cache, cacheKey, TimeSpan.FromSeconds(30), ShouldBypassCache(ctx.Request),
                () => repo.GetOrdersAsync(serviceId, excludeProfessionalId, professionalId,
                                          ParseBoolParam(filterZones), ParseBoolParam(active), effPage, effPageSize, ct),
                logger: null, ct);

            var orders = paged.Items;
            var orderIds = orders.Select(o => o.Id).ToArray();
            var reviewByOrder = new Dictionary<string, (string Id, int Rating)>();
            if (orderIds.Length > 0)
            {
                var reviews = await dbCtx.Reviews
                    .AsNoTracking()
                    .Where(r => orderIds.Contains(r.OrderId))
                    .Select(r => new { r.OrderId, r.Id, r.Rating })
                    .ToListAsync(ct);
                reviewByOrder = reviews.ToDictionary(r => r.OrderId, r => (r.Id, r.Rating));
            }

            var requestingClientId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                                  ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? ctx.Request.Query["clientId"].FirstOrDefault();

            var now = DateTime.UtcNow;
            var result = orders.Select(o =>
            {
                var windowOk = (o.CompletedAt ?? o.CreatedAt) >= now.AddDays(-30);
                var hasReview = reviewByOrder.TryGetValue(o.Id, out var rev);
                var canReview = o.Status == OrderStatus.Completed
                    && o.ClientId == requestingClientId
                    && !hasReview
                    && windowOk;
                return (object)new
                {
                    id = o.Id,
                    clientId = o.ClientId,
                    serviceId = o.ServiceId,
                    description = o.Description,
                    location = o.Location,
                    date = o.Date,
                    status = o.Status,
                    createdAt = o.CreatedAt,
                    professionalId = o.ProfessionalId,
                    tierId = o.TierId,
                    origin = o.Origin,
                    proposalId = o.ProposalId,
                    appointmentId = o.AppointmentId,
                    conversationId = o.ConversationId,
                    priceTotalCents = o.PriceTotalCents,
                    signalCents = o.SignalCents,
                    balanceCents = o.BalanceCents,
                    installments = o.Installments,
                    paymentMethod = o.PaymentMethod,
                    addressId = o.AddressId,
                    scope = o.Scope,
                    scheduledAt = o.ScheduledAt,
                    completedAt = o.CompletedAt,
                    cancelledAt = o.CancelledAt,
                    cancelledBy = o.CancelledBy,
                    cancellationReason = o.CancellationReason,
                    autoConfirmAt = o.AutoConfirmAt,
                    svcAddrZipCode = o.SvcAddrZipCode,
                    svcAddrStreet = o.SvcAddrStreet,
                    svcAddrNumber = o.SvcAddrNumber,
                    svcAddrNeighborhood = o.SvcAddrNeighborhood,
                    svcAddrCity = o.SvcAddrCity,
                    svcAddrState = o.SvcAddrState,
                    svcAddrComplement = o.SvcAddrComplement,
                    svcAddrReference = o.SvcAddrReference,
                    recurringPlanId = o.RecurringPlanId,
                    platformFeePercent = o.PlatformFeePercent,
                    platformFeeCents = o.PlatformFeeCents,
                    gatewayFeeCents = o.GatewayFeeCents,
                    paymentStatus = o.PaymentStatus,
                    mpPreferenceId = o.MpPreferenceId,
                    canReview,
                    reviewId = hasReview ? rev.Id : (string?)null,
                    reviewRating = hasReview ? (int?)rev.Rating : null,
                };
            }).ToList();

            SetPaginationHeaders(ctx.Response, paged.TotalCount, effPage, effPageSize);
            return Results.Ok(result);
        });

        app.MapPost("/orders", async (
            CreateOrderRequest body,
            HttpContext context,
            IValidator<CreateOrderRequest> validator,
            IOrderRepository repo,
            CancellationToken ct) =>
        {
            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (jwtUserId is null)
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            // clientId sempre vem do JWT autenticado, exceto quando o chamador é admin
            // e envia clientId explicitamente (criação em nome de terceiros).
            var clientId = AuthorizationHelpers.IsAdmin(context) && !string.IsNullOrWhiteSpace(body.ClientId)
                ? body.ClientId
                : jwtUserId;
            body = body with { ClientId = clientId };

            var val = await validator.ValidateAsync(body, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            var date = DateTime.TryParse(body.Date, out var parsed) ? parsed : (DateTime?)null;
            var created = await repo.CreateAsync(body.ClientId, body.ServiceId, body.Description, body.Location, date, ct);
            return Results.Json(created, statusCode: 201);
        });

        app.MapGet("/orders/mine", async (HttpContext ctx, string clientId, int? page, int? pageSize, IOrderRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);

            var effPage = ClampPage(page);
            var effPageSize = ClampPageSize(pageSize);
            var paged = await repo.GetMineAsync(clientId, effPage, effPageSize, ct);
            SetPaginationHeaders(ctx.Response, paged.TotalCount, effPage, effPageSize);
            return Results.Ok(paged.Items);
        });

        app.MapPost("/orders/{id}/complete", async (
            string id,
            CompleteOrderRequest _,
            HttpContext context,
            IOrderRepository repo,
            AppDbContext dbCtx,
            CancellationToken ct) =>
        {
            var order = await repo.GetByIdAsync(id, ct);
            if (order is null) return Results.NotFound(new { error = "Pedido não encontrado" });

            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (jwtUserId is null)
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            var isClient = order.ClientId == jwtUserId;
            var isProfessional = order.ProfessionalId is not null && await dbCtx.Professionals.AnyAsync(
                p => p.Id == order.ProfessionalId && (p.UserId == jwtUserId || p.Id == jwtUserId), ct);

            if (!AuthorizationHelpers.IsAdmin(context) && !isClient && !isProfessional)
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            await repo.CompleteOrderAsync(id, ct);
            return Results.Ok(new { ok = true });
        });

        // ─── Appointments ──────────────────────────────────────────────────────
        app.MapGet("/appointments", async (
            string? professionalId, string? status, string? from, string? to,
            IAppointmentRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório." }, statusCode: 400);
            var fromDt = DateTime.TryParse(from, out var fp) ? fp : (DateTime?)null;
            var toDt = DateTime.TryParse(to, out var tp) ? tp : (DateTime?)null;
            return Results.Ok(await repo.GetByProfessionalAsync(professionalId, status, fromDt, toDt, ct));
        });

        app.MapGet("/appointments/mine", async (string clientId, IAppointmentRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetByClientAsync(clientId, ct)));

        app.MapGet("/appointments/slots", async (
            string? professionalId, string? date, string? day,
            string? professionalServiceId,
            IAvailabilityRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            date ??= day;
            if (string.IsNullOrWhiteSpace(date) || !System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
                return Results.Json(new { error = "date (ou day) deve ser YYYY-MM-DD" }, statusCode: 400);

            var config = await repo.GetProfessionalSchedulingConfigAsync(professionalId, ct);
            if (config is null) return Results.NotFound(new { error = "Profissional não encontrado" });

            var c = ToObjectDictionary(config);
            // Fallback chain: ProfessionalService.DurationMinutes → Professional.SlotMinutes → 60
            int? serviceDuration = null;
            if (!string.IsNullOrWhiteSpace(professionalServiceId))
                serviceDuration = await repo.GetProfessionalServiceDurationAsync(professionalServiceId, ct);
            var slotMinutes = (serviceDuration is > 0 ? (int?)serviceDuration : null)
                           ?? (c["slotMinutes"] is null ? (int?)null : Convert.ToInt32(c["slotMinutes"]))
                           ?? 60;
            var leadTimeMinutes = c["leadTimeMinutes"] is null ? 0 : Convert.ToInt32(c["leadTimeMinutes"]);
            var maxAdvanceDays = c["maxAdvanceDays"] is null ? 30 : Convert.ToInt32(c["maxAdvanceDays"]);

            var parts = date.Split('-').Select(int.Parse).ToArray();
            var targetDate = new DateTime(parts[0], parts[1], parts[2], 0, 0, 0, DateTimeKind.Utc);
            var diffDays = (targetDate - DateTime.UtcNow.Date).Days;
            if (diffDays > maxAdvanceDays)
                return Results.Ok(new { slots = Array.Empty<object>(), slotMinutes, timezone = "America/Sao_Paulo" });

            // São Paulo = UTC-3
            const int SpOffsetMin = 180;
            var spOffset = TimeSpan.FromHours(-3);
            var dayStartUtc = targetDate.AddMinutes(SpOffsetMin); // midnight SP = 03:00Z
            var dayEndUtc = dayStartUtc.AddDays(1).AddMilliseconds(-1);
            var weekday = (int)targetDate.DayOfWeek;

            var availabilities = await repo.GetAvailabilityForDayAsync(professionalId, weekday, ct);
            var appointments = await repo.GetAppointmentsForDayAsync(professionalId, dayStartUtc, dayEndUtc, ct);
            var blocks = await repo.GetBlocksForDayAsync(professionalId, dayStartUtc, dayEndUtc, ct);

            var minStart = DateTime.UtcNow.AddMinutes(leadTimeMinutes);
            var slots = new List<object>();

            foreach (var avail in availabilities)
            {
                var rangeStartUtc = dayStartUtc.AddMinutes(avail.StartMinutes);
                var rangeEndUtc = dayStartUtc.AddMinutes(avail.EndMinutes);
                var cursor = rangeStartUtc;

                while (cursor < rangeEndUtc)
                {
                    var slotEnd = cursor.AddMinutes(slotMinutes);
                    if (slotEnd > rangeEndUtc) break;
                    if (cursor < minStart) { cursor = slotEnd; continue; }

                    var hasConflict = appointments.Any(a => cursor < a.EndsAt && a.StartsAt < slotEnd)
                                   || blocks.Any(b => cursor < b.EndsAt && b.StartsAt < slotEnd);

                    if (!hasConflict)
                        slots.Add(new {
                            start = new DateTimeOffset(cursor, TimeSpan.Zero).ToOffset(spOffset).ToString("yyyy-MM-ddTHH:mm:sszzz"),
                            end   = new DateTimeOffset(slotEnd, TimeSpan.Zero).ToOffset(spOffset).ToString("yyyy-MM-ddTHH:mm:sszzz")
                        });
                    cursor = slotEnd;
                }
            }

            return Results.Ok(new { slots, slotMinutes, timezone = "America/Sao_Paulo" });
        });

        app.MapPost("/appointments", async (
            CreateAppointmentRequest body, IAppointmentRepository repo, IEmailService email, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(body.ProfessionalId, ct))
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 404);
            if (body.StartsAt < DateTime.UtcNow)
                return Results.Json(new { error = "Horário no passado." }, statusCode: 400);

            var (slotMinutes, allowInstantBooking) = await repo.GetProfessionalConfigAsync(body.ProfessionalId, ct);
            var endsAt = body.EndsAt == default || body.EndsAt <= body.StartsAt
                ? body.StartsAt.AddMinutes(slotMinutes ?? 60)
                : body.EndsAt;

            if (await repo.HasConflictAsync(body.ProfessionalId, body.StartsAt, endsAt, ct))
                return Results.Json(new { error = "Horário indisponível." }, statusCode: 409);

            var status = allowInstantBooking == true ? "CONFIRMED" : "PENDING";
            var created = await repo.CreateAsync(new Appointment(
                Guid.NewGuid().ToString(), body.ProfessionalId, body.ClientId, body.ServiceId,
                body.StartsAt, endsAt, status, body.Location, body.Notes), ct);

            if (status == "CONFIRMED")
                await SendBookingEmailsAsync(email, repo, created.Id, created.StartsAt, ct);

            return Results.Json(created, statusCode: 201);
        });

        app.MapPut("/appointments/{id}", async (
            string id, UpdateAppointmentStatusRequest body, IAppointmentRepository repo, IEmailService email, CancellationToken ct) =>
        {
            var allowed = new[] { "CONFIRMED", "CANCELLED" };
            if (!allowed.Contains(body.Status, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Status inválido." }, statusCode: 400);
            var statusUpper = body.Status.ToUpperInvariant();
            var updated = await repo.UpdateStatusAsync(id, statusUpper, ct);
            if (updated is null) return Results.NotFound(new { error = "Agendamento não encontrado." });
            if (statusUpper == "CONFIRMED")
                await SendBookingEmailsAsync(email, repo, id, updated.StartsAt, ct);
            return Results.Ok(updated);
        });

        // ─── Conversations ─────────────────────────────────────────────────────
        app.MapGet("/conversations", async (HttpRequest req, IConversationRepository repo, CancellationToken ct) =>
        {
            var jwtUserId = req.HttpContext.User?.FindFirst("sub")?.Value
                         ?? req.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(jwtUserId))
                return Results.Json(new { error = "Não autenticado" }, statusCode: 401);

            var role = req.HttpContext.User?.FindFirst("role")?.Value ?? "cliente";
            var data = role == "cliente"
                ? await repo.GetByParticipantAsync(jwtUserId, null, ct)
                : await repo.GetByParticipantAsync(null, jwtUserId, ct);
            return Results.Ok(data);
        });

        app.MapPost("/conversations", async (CreateConversationRequest body, IConversationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ClientId) || string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "clientId e professionalId são obrigatórios" }, statusCode: 400);
            var professionalUserId = await repo.ResolveProfessionalUserIdAsync(body.ProfessionalId, ct);
            if (professionalUserId is null)
                return Results.Json(new { error = "Profissional não encontrado" }, statusCode: 400);
            var conv = await repo.GetOrCreateAsync(body.ClientId, professionalUserId, body.OrderId, ct);
            return Results.Ok(conv);
        });

        // ─── Messages ──────────────────────────────────────────────────────────
        app.MapGet("/messages", async (
            string? conversationId,
            string? since,
            HttpRequest req,
            IConversationRepository repo,
            CancellationToken ct) =>
        {
            var jwtUserId = req.HttpContext.User?.FindFirst("sub")?.Value
                         ?? req.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(jwtUserId))
                return Results.Json(new { error = "Não autenticado" }, statusCode: 401);

            if (string.IsNullOrWhiteSpace(conversationId))
                return Results.Json(new { error = "conversationId é obrigatório" }, statusCode: 400);

            var conv = await repo.GetConversationForReadAsync(conversationId, ct);
            if (conv is null)
                return Results.NotFound(new { error = "Conversa não encontrada" });

            var c = ToObjectDictionary(conv);
            if (c["clientId"]?.ToString() != jwtUserId && c["professionalId"]?.ToString() != jwtUserId)
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            DateTime? sinceDate = null;
            if (!string.IsNullOrWhiteSpace(since))
            {
                if (DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    sinceDate = parsed.ToUniversalTime();
            }

            return Results.Ok(await repo.GetMessagesAsync(conversationId, ct, sinceDate));
        });

        // Phase 2: POST /messages accepts type, metadata, replyToId + anti-leak detection
        app.MapPost("/messages", async (
            HttpRequest req,
            IConversationRepository repo,
            IEmailService emailSvc,
            IAntiLeakDetectionService antiLeak,
            IProfessionalDetailRepository userRepo,
            CancellationToken ct) =>
        {
            var body = await ParseSendMessageRequestAsync(req, ct);
            if (body is null)
                return Results.Json(new { error = "Body inválido. Envie JSON no formato { conversationId, senderId, text, ... }" }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(body.ConversationId) || string.IsNullOrWhiteSpace(body.Text))
                return Results.Json(new { error = "conversationId e text são obrigatórios" }, statusCode: 400);

            // Resolve senderId: prefer JWT sub claim (Supabase Auth UUID) over client-provided value
            var jwtUserId = req.HttpContext.User?.FindFirst("sub")?.Value
                         ?? req.HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var resolvedSenderId = !string.IsNullOrWhiteSpace(jwtUserId) ? jwtUserId : body.SenderId;

            if (string.IsNullOrWhiteSpace(resolvedSenderId))
                return Results.Json(new { error = "senderId é obrigatório" }, statusCode: 400);

            // Validate sender exists in User table before persisting (prevents FK violation)
            if (!await userRepo.UserExistsAsync(resolvedSenderId, ct))
                return Results.Json(new { error = "Remetente inválido: usuário não encontrado" }, statusCode: 422);

            // Verify sender is a participant of the conversation before persisting
            var conv = await repo.GetConversationForReadAsync(body.ConversationId, ct);
            if (conv is null)
                return Results.Json(new { error = "Conversa não encontrada" }, statusCode: 404);

            var convDict = ToObjectDictionary(conv);
            if (convDict["clientId"]?.ToString() != resolvedSenderId && convDict["professionalId"]?.ToString() != resolvedSenderId)
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            var messageType = string.IsNullOrWhiteSpace(body.Type) ? MessageType.Text : body.Type;

            var message = await repo.CreateMessageAsync(
                body.ConversationId,
                resolvedSenderId,
                body.Text.Trim(),
                messageType,
                body.Metadata,
                body.ReplyToId,
                ct);

            var msgDict = ToObjectDictionary(message);

            // Phase 2: anti-leak detection — insert a system warning message if contact patterns detected
            if (messageType == MessageType.Text && antiLeak.HasLeakPattern(body.Text))
            {
                _ = repo.CreateMessageAsync(
                    body.ConversationId,
                    resolvedSenderId,
                    antiLeak.GetWarningText(),
                    MessageType.System,
                    null,
                    null,
                    ct).ConfigureAwait(false);

                // Flag conversation when leak patterns detected
                _ = repo.UpdateConversationStatusAsync(body.ConversationId, ConversationStatus.Flagged, ct).ConfigureAwait(false);
            }

            var isClient = resolvedSenderId == convDict["clientId"]?.ToString();
            var lastReadAt = isClient ? convDict["professionalLastReadAt"] : convDict["clientLastReadAt"];
            var recipientEmail = isClient ? convDict["professionalEmail"]?.ToString() : convDict["clientEmail"]?.ToString();
            var recipientName = isClient ? convDict["professionalName"]?.ToString() ?? "Usuário" : convDict["clientName"]?.ToString() ?? "Usuário";
            var senderName = msgDict["senderName"]?.ToString() ?? "Usuário";

            var recentlyActive = lastReadAt is DateTime lra && (DateTime.UtcNow - lra).TotalMilliseconds <= 120_000;
            if (!recentlyActive && !string.IsNullOrWhiteSpace(recipientEmail))
            {
                var appBaseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "https://jobeasy.com.br";
                _ = emailSvc.SendChatMessageAsync(recipientEmail, recipientName, senderName,
                    body.Text.Trim(), $"{appBaseUrl}/chat/{body.ConversationId}",
                    body.ConversationId, windowMinutes: 10, ct).ConfigureAwait(false);
            }

            return Results.Ok(message);
        });

        // Phase 2: POST /messages/attachment — upload de anexo de chat (multipart/form-data)
        app.MapPost("/messages/attachment", async (
            HttpRequest req,
            IConversationRepository convRepo,
            IAttachmentStorageRepository storageRepo,
            IMessageAttachmentRepository attachmentRepo,
            CancellationToken ct) =>
        {
            const long maxSizeBytes = 20 * 1024 * 1024; // 20 MB
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "image/jpeg", "image/png", "image/webp", "image/gif",
                "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "video/mp4", "video/quicktime"
            };

            if (!req.HasFormContentType)
                return Results.Json(new { error = "Content-Type deve ser multipart/form-data." }, statusCode: 400);

            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            var conversationId = (form["conversationId"].FirstOrDefault() ?? string.Empty).Trim();
            var senderId = (form["senderId"].FirstOrDefault() ?? string.Empty).Trim();
            var messageText = (form["text"].FirstOrDefault() ?? string.Empty).Trim();
            var attachmentType = (form["attachmentType"].FirstOrDefault() ?? "file").Trim();

            if (file is null || file.Length == 0)
                return Results.Json(new { error = "Arquivo não enviado." }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(conversationId))
                return Results.Json(new { error = "conversationId é obrigatório." }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(senderId))
                return Results.Json(new { error = "senderId é obrigatório." }, statusCode: 400);
            if (file.Length > maxSizeBytes)
                return Results.Json(new { error = "Arquivo excede 20MB." }, statusCode: 400);

            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            if (!allowedTypes.Contains(contentType))
                return Results.Json(new { error = "Tipo de arquivo não permitido." }, statusCode: 400);

            // Create the message first (with type = "action" and empty text fallback)
            var text = string.IsNullOrWhiteSpace(messageText) ? $"[Arquivo: {file.FileName}]" : messageText;
            var message = await convRepo.CreateMessageAsync(conversationId, senderId, text, MessageType.Action, null, null, ct);
            var msgDict = ToObjectDictionary(message);
            var messageId = msgDict["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(messageId))
                return Results.Json(new { error = "Falha ao criar mensagem para upload." }, statusCode: 500);

            // Upload file to Supabase Storage
            await using var fileStream = file.OpenReadStream();
            var publicUrl = await storageRepo.UploadAsync(messageId, fileStream, contentType, file.FileName ?? "file", ct);

            if (string.IsNullOrWhiteSpace(publicUrl))
                return Results.Json(new { error = "Falha no upload do arquivo." }, statusCode: 500);

            // Determine if image type for thumbnail (same URL for now — thumbnail can be generated async)
            var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            var attachment = new MessageAttachment(
                Id: Guid.NewGuid().ToString(),
                MessageId: messageId,
                Type: attachmentType,
                Url: publicUrl,
                ThumbnailUrl: isImage ? publicUrl : null,
                FileName: file.FileName,
                SizeBytes: (int)file.Length,
                CreatedAt: DateTime.UtcNow);

            await attachmentRepo.CreateAsync(attachment, ct);

            return Results.Ok(new
            {
                message = msgDict,
                attachment = new
                {
                    id = attachment.Id,
                    messageId = attachment.MessageId,
                    type = attachment.Type,
                    url = attachment.Url,
                    thumbnailUrl = attachment.ThumbnailUrl,
                    fileName = attachment.FileName,
                    sizeBytes = attachment.SizeBytes
                }
            });
        });

        app.MapPost("/chat/read", async (MarkReadRequest body, IConversationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ConversationId) || string.IsNullOrWhiteSpace(body.UserId))
                return Results.Json(new { error = "conversationId e userId são obrigatórios." }, statusCode: 400);
            var conv = await repo.GetConversationForReadAsync(body.ConversationId, ct);
            if (conv is null) return Results.NotFound(new { error = "Conversa não encontrada." });
            var c = ToObjectDictionary(conv);
            if (body.UserId == c["clientId"]?.ToString())
                await repo.MarkReadAsync(body.ConversationId, isClient: true, ct);
            else if (body.UserId == c["professionalId"]?.ToString())
                await repo.MarkReadAsync(body.ConversationId, isClient: false, ct);
            else
                return Results.Json(new { error = "userId não pertence à conversa." }, statusCode: 403);
            return Results.Ok(new { ok = true });
        });

        // Phase 2: GET /conversations/{id}/actions — transactional actions available in the conversation
        app.MapGet("/conversations/{id}/actions", async (
            string id,
            string requestingUserId,
            IConversationRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(requestingUserId))
                return Results.Json(new { error = "requestingUserId é obrigatório" }, statusCode: 400);
            var actions = await repo.GetConversationActionsAsync(id, requestingUserId, ct);
            return Results.Ok(actions);
        });

        // Phase 2: PATCH /conversations/{id}/status — update conversation status (active/archived/flagged)
        app.MapPatch("/conversations/{id}/status", async (
            string id,
            UpdateConversationStatusRequest body,
            IConversationRepository repo,
            CancellationToken ct) =>
        {
            var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ConversationStatus.Active, ConversationStatus.Archived, ConversationStatus.Flagged };

            if (!validStatuses.Contains(body.Status))
                return Results.Json(new { error = $"Status inválido. Use: {string.Join(", ", validStatuses)}" }, statusCode: 400);

            await repo.UpdateConversationStatusAsync(id, body.Status.ToLowerInvariant(), ct);
            return Results.Ok(new { ok = true, status = body.Status.ToLowerInvariant() });
        });

        // ─── Reviews ───────────────────────────────────────────────────────────
        static async Task<IResult> GetReviewsAsync(string? professionalId, int? limit, IReviewRepository repo, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetByProfessionalAsync(professionalId, limit ?? 10, ct));
        }

        //app.MapGet("/reviews", GetReviewsAsync);
        app.MapGet("/reviews", GetReviewsAsync);

        app.MapPost("/reviews", async (CreateReviewRequest body, IReviewRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "Campos obrigatórios ausentes" }, statusCode: 400);
            if (body.Rating < 1 || body.Rating > 5)
                return Results.Json(new { error = "rating deve ser 1..5" }, statusCode: 400);
            var orderId = body.OrderId?.Trim();
            if (string.IsNullOrWhiteSpace(orderId))
                return Results.Json(new { error = "orderId é obrigatório" }, statusCode: 400);
            if (!await repo.OrderBelongsToClientAsync(orderId, body.ClientId, ct))
                return Results.Json(new { error = "Pedido inválido para este cliente" }, statusCode: 400);
            if (await repo.OrderAlreadyReviewedAsync(orderId, ct))
                return Results.Json(new { error = "Este pedido já foi avaliado" }, statusCode: 400);
            return Results.Ok(await repo.CreateAsync(body.ProfessionalId, body.ClientId, orderId, body.Rating, body.Comment, ct));
        });

        app.MapGet("/reviews/eligible-orders", async (string? clientId, string? professionalId, IReviewRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId e clientId são obrigatórios" }, statusCode: 400);
            return Results.Ok(await repo.GetEligibleOrdersAsync(clientId, professionalId, ct));
        });

        static async Task<IResult> GetReviewByIdAsync(string id, IReviewRepository repo, CancellationToken ct)
        {
            var review = await repo.GetByIdAsync(id, ct);
            return review is null ? Results.NotFound(new { error = "Avaliação não encontrada" }) : Results.Ok(review);
        }

        //app.MapGet("/reviews/{id}", GetReviewByIdAsync);
        app.MapGet("/reviews/{id}", GetReviewByIdAsync);

        static async Task<IResult> PatchReviewAsync(string id, UpdateReviewRequest body, IReviewRepository repo, CancellationToken ct)
        {
            var updated = await repo.UpdateAsync(id, body.Rating, body.Comment, ct);
            return updated is null ? Results.NotFound(new { error = "Avaliação não encontrada" }) : Results.Ok(updated);
        }

        app.MapMethods("/reviews/{id}", ["PATCH"], PatchReviewAsync);
        app.MapMethods("/reviews/{id}", ["PATCH"], PatchReviewAsync);

        // ─── Phase 3: Expanded Reviews ─────────────────────────────────────────

        // POST /reviews/expanded — client reviews with categories + photos
        app.MapPost("/reviews/expanded", async (CreateExpandedReviewRequest body, IReviewRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "Campos obrigatórios ausentes" }, statusCode: 400);
            if (body.Rating < 1 || body.Rating > 5)
                return Results.Json(new { error = "rating deve ser 1..5" }, statusCode: 400);
            foreach (var cat in new[] { body.PunctualityRating, body.QualityRating, body.CommunicationRating, body.CleanlinessRating })
                if (cat is < 1 or > 5)
                    return Results.Json(new { error = "Categorias de nota devem ser 1..5" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.OrderId))
                return Results.Json(new { error = "orderId é obrigatório" }, statusCode: 400);
            if (!await repo.OrderBelongsToClientAsync(body.OrderId, body.ClientId, ct))
                return Results.Json(new { error = "Pedido inválido para este cliente" }, statusCode: 400);
            if (await repo.OrderAlreadyReviewedAsync(body.OrderId, ct))
                return Results.Json(new { error = "Este pedido já foi avaliado" }, statusCode: 400);

            var photoUrlsJson = body.PhotoUrls?.Length > 0
                ? System.Text.Json.JsonSerializer.Serialize(body.PhotoUrls)
                : null;

            return Results.Ok(await repo.CreateExpandedAsync(
                body.ProfessionalId, body.ClientId, body.OrderId,
                body.Rating, body.Comment,
                body.PunctualityRating, body.QualityRating,
                body.CommunicationRating, body.CleanlinessRating,
                photoUrlsJson, isVerified: false, ct));
        });

        // POST /reviews/professional — professional reviews client
        app.MapPost("/reviews/professional", async (ProfessionalReviewClientRequest body, IReviewRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.OrderId))
                return Results.Json(new { error = "professionalId e orderId são obrigatórios" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.Review))
                return Results.Json(new { error = "review é obrigatório" }, statusCode: 400);
            if (body.Rating is < 1 or > 5)
                return Results.Json(new { error = "rating deve ser 1..5" }, statusCode: 400);
            if (!await repo.OrderBelongsToProfessionalAsync(body.OrderId, body.ProfessionalId, ct))
                return Results.Json(new { error = "Pedido inválido para este profissional" }, statusCode: 400);
            if (await repo.ProfessionalAlreadyReviewedClientAsync(body.OrderId, ct))
                return Results.Json(new { error = "Profissional já avaliou este pedido" }, statusCode: 400);

            try
            {
                return Results.Ok(await repo.AddProfessionalReviewOfClientAsync(body.OrderId, body.ProfessionalId, body.Review, body.Rating, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 422);
            }
        });

        // ─── Portfolio ─────────────────────────────────────────────────────────
        app.MapGet("/portfolio", async (string? professionalId, IPortfolioRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetByProfessionalAsync(professionalId, ct));
        });

        app.MapPost("/portfolio", async (CreatePortfolioItemRequest body, IPortfolioRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.ImageUrl))
                return Results.Json(new { error = "professionalId e imageUrl são obrigatórios" }, statusCode: 400);
            return Results.Json(await repo.CreateAsync(body.ProfessionalId, body.ImageUrl, body.Title, body.Description, ct), statusCode: 201);
        });

        app.MapGet("/portfolio/{id}", async (string id, IPortfolioRepository repo, CancellationToken ct) =>
        {
            var item = await repo.GetByIdAsync(id, ct);
            return item is null ? Results.NotFound(new { error = "Item não encontrado" }) : Results.Ok(item);
        });

        app.MapPut("/portfolio/{id}", async (string id, UpdatePortfolioItemRequest body, IPortfolioRepository repo, CancellationToken ct) =>
        {
            var updated = await repo.UpdateAsync(id, body.Title, body.Description, body.ImageUrl, body.OrderIndex, ct);
            return updated is null ? Results.NotFound(new { error = "Item não encontrado" }) : Results.Ok(updated);
        });

        app.MapDelete("/portfolio/{id}", async (string id, IPortfolioRepository repo, CancellationToken ct) =>
        {
            var deleted = await repo.DeleteAsync(id, ct);
            return deleted ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "Item não encontrado" });
        });

        // ─── Availability ──────────────────────────────────────────────────────
        app.MapGet("/pro-availability/{id}", async (string id, IAvailabilityRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetByProfessionalAsync(id, ct)));

        app.MapMethods("/pro-availability/{id}", ["PUT", "POST"], async (string id, SaveAvailabilityRequest body, IAvailabilityRepository repo, CancellationToken ct) =>
        {
            var rawRows = body.Items ?? body.Rows ?? [];
            var validRows = rawRows
                .Where(r => r.Weekday >= 0 && r.Weekday <= 6 && r.StartMinutes >= 0 && r.EndMinutes <= 1440 && r.EndMinutes > r.StartMinutes)
                .Select(r => (r.Weekday, r.StartMinutes, r.EndMinutes, r.Active))
                .ToList();
            if (validRows.Count == 0)
                return Results.Json(new { error = "Itens de disponibilidade inválidos" }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(id, ct))
                return Results.NotFound(new { error = "Profissional não encontrado" });
            await repo.SaveAllAsync(id, validRows, ct);
            return Results.Ok(new { ok = true });
        });

        // ─── Professional Blocks ────────────────────────────────────────────────
        app.MapGet("/professional-blocks", async (string? professionalId, string? from, string? to, IAvailabilityRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            var fromDt = DateTime.TryParse(from, out var fp) ? fp : DateTime.UtcNow;
            var toDt = DateTime.TryParse(to, out var tp) ? tp : DateTime.UtcNow.AddDays(30);
            return Results.Ok(await repo.GetBlocksAsync(professionalId, fromDt, toDt, ct));
        });

        app.MapPost("/professional-blocks", async (CreateBlockRequest body, IAvailabilityRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (!DateTime.TryParse(body.StartsAt, out var startsAt) || !DateTime.TryParse(body.EndsAt, out var endsAt) || endsAt <= startsAt)
                return Results.Json(new { error = "Dados inválidos" }, statusCode: 400);
            return Results.Json(await repo.CreateBlockAsync(body.ProfessionalId, startsAt, endsAt, body.Reason, ct), statusCode: 201);
        });

        // ─── Order Ignores ─────────────────────────────────────────────────────
        app.MapPost("/order-ignores", async (CreateOrderIgnoreRequest body, IOrderIgnoreRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.OrderId))
                return Results.Json(new { error = "professionalId e orderId são obrigatórios." }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(body.ProfessionalId, ct))
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 400);
            if (!await repo.OrderExistsAsync(body.OrderId, ct))
                return Results.Json(new { error = "Pedido não encontrado." }, statusCode: 400);
            await repo.UpsertAsync(body.ProfessionalId, body.OrderId, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapDelete("/order-ignores", async (string? professionalId, string? orderId, IOrderIgnoreRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId) || string.IsNullOrWhiteSpace(orderId))
                return Results.Json(new { error = "professionalId e orderId são obrigatórios." }, statusCode: 400);
            await repo.DeleteAsync(professionalId, orderId, ct);
            return Results.Ok(new { ok = true });
        });

        // Phase 1 endpoints
        app.MapOrderEndpoints();
        app.MapProposalEndpoints();

        // Phase 3 endpoints
        app.MapDisputeEndpoints();

        // Phase 4 endpoints
        app.MapRecurringEndpoints();

        // Phase 5 endpoints
        app.MapVerificationEndpoints();

        // MP Payment (PRD-MP-03)
        app.MapPaymentEndpoints();

        // MP Webhook (PRD-MP-04)
        app.MapWebhookEndpoints();

        // Wallet (PRD-MP-05)
        app.MapWalletEndpoints();

        // Admin panel backend
        app.MapAdminEndpoints();

        return app;
    }

    // ─── Private helpers ────────────────────────────────────────────────────────
    private static async Task SendBookingEmailsAsync(IEmailService email, IAppointmentRepository repo, string appointmentId, DateTime startsAt, CancellationToken ct)
    {
        var apptData = await repo.GetAppointmentWithParticipantsAsync(appointmentId, ct);
        if (apptData is null) return;
        var d = ToObjectDictionary(apptData);
        var when = startsAt.ToString("ddd, dd/MM/yyyy HH:mm");
        var appBaseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "https://jobeasy.com.br";
        var bookingUrl = $"{appBaseUrl}/agenda/{appointmentId}";
        var dedupeBase = $"booking.confirmed|{appointmentId}";
        var proEmail = d["professionalEmail"]?.ToString();
        var clientEmail = d["clientEmail"]?.ToString();
        if (!string.IsNullOrWhiteSpace(proEmail))
            _ = email.SendBookingConfirmedProfessionalAsync(proEmail,
                d["professionalName"]?.ToString() ?? "", d["clientName"]?.ToString() ?? "",
                d["serviceName"]?.ToString() ?? "Serviço", when, bookingUrl,
                dedupeKey: $"{dedupeBase}|pro|{proEmail}", ct: ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(clientEmail))
            _ = email.SendBookingConfirmedClientAsync(clientEmail,
                d["clientName"]?.ToString() ?? "", d["professionalName"]?.ToString() ?? "",
                d["serviceName"]?.ToString() ?? "Serviço", when, bookingUrl,
                dedupeKey: $"{dedupeBase}|cli|{clientEmail}", ct: ct).ConfigureAwait(false);
    }

    private static async Task<IResult?> RequireProfessionalServiceOwnerOrAdminAsync(
        string professionalServiceId, HttpContext context, AppDbContext ctx, CancellationToken ct)
    {
        var svc = await ctx.ProfessionalServices.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == professionalServiceId, ct);
        if (svc is null)
            return Results.NotFound(new { error = "Serviço não encontrado" });

        var professional = await ctx.Professionals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == svc.ProfessionalId, ct);
        if (professional is null || !AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
            return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

        return null;
    }

    private static IResult? RequireSelf(HttpContext context, string routeId, out string jwtUserId)
    {
        jwtUserId = context.User?.FindFirst("sub")?.Value
                 ?? context.User?.FindFirst(
                     System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? "";

        if (string.IsNullOrWhiteSpace(jwtUserId))
            return Results.Json(new { error = "Não autenticado" }, statusCode: 401);

        if (!string.Equals(jwtUserId, routeId, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Não autorizado" }, statusCode: 403);

        return null;
    }

    private static string? GenerateJwt(IConfiguration config, object userObj)
    {
        var jwtSecret = config["JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(jwtSecret))
            return null;

        var userJson = JsonSerializer.Serialize(userObj);
        using var userDoc = JsonDocument.Parse(userJson);
        var root = userDoc.RootElement;
        var userId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var email = root.TryGetProperty("email", out var emailEl) ? emailEl.GetString() ?? "" : "";
        var role = root.TryGetProperty("role", out var roleEl) ? roleEl.GetString() ?? "" : "";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        var jwtToken = new JwtSecurityToken(
            issuer: "jobeasy",
            audience: "jobeasy",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwtToken);
    }

    private static bool ParseBoolParam(string? value)
        => value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldBypassCache(HttpRequest req)
        => req.Headers.TryGetValue("Cache-Control", out var values)
           && values.Any(v => v?.Contains("no-cache", StringComparison.OrdinalIgnoreCase) == true);

    // ─── Pagination helpers (PRD Performance/Escalabilidade — Item 1) ─────────
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    private static int ClampPage(int? page) => page is > 0 ? page.Value : 1;

    private static int ClampPageSize(int? pageSize) => pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : DefaultPageSize;

    private static void SetPaginationHeaders(HttpResponse response, int totalCount, int page, int pageSize)
    {
        response.Headers["X-Total-Count"] = totalCount.ToString();
        response.Headers["X-Page"] = page.ToString();
        response.Headers["X-Page-Size"] = pageSize.ToString();
    }

    private static async Task<SendMessageRequest?> ParseSendMessageRequestAsync(HttpRequest req, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (req.Body is null)
            return null;

        if (req.Body.CanSeek)
            req.Body.Position = 0;

        using var reader = new StreamReader(req.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(ct);

        if (req.Body.CanSeek)
            req.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        try
        {
            var direct = JsonSerializer.Deserialize<SendMessageRequest>(payload, jsonOptions);
            if (direct is not null)
                return direct;
        }
        catch (JsonException)
        {
            // Fallback below for double-encoded JSON payloads.
        }

        try
        {
            var innerPayload = JsonSerializer.Deserialize<string>(payload, jsonOptions);
            if (string.IsNullOrWhiteSpace(innerPayload))
                return null;

            return JsonSerializer.Deserialize<SendMessageRequest>(innerPayload, jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IDictionary<string, object?> ToObjectDictionary(object? value)
    {
        if (value is null)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (value is IDictionary<string, object?> dictionary)
            return dictionary;

        if (value is IDictionary<string, object> nonNullableDictionary)
        {
            return nonNullableDictionary
                .ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value, StringComparer.OrdinalIgnoreCase);
        }

        return value
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(p => p.Name, p => p.GetValue(value), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IResult> GetOrSetCachedListAsync<T>(
        HttpContext context, IMemoryCache cache, string keyPrefix, TimeSpan ttl,
        Func<CancellationToken, Task<IReadOnlyList<T>>> factory, CancellationToken ct)
    {
        var cacheKey = $"{keyPrefix}:{context.Request.QueryString.Value ?? string.Empty}";
        var items = await GetOrCreateCachedAsync(cache, cacheKey, ttl, ShouldBypassCache(context.Request), () => factory(ct), logger: null, ct);
        return Results.Ok(items);
    }

    private static Task<string?> ResolveProfessionalIdAsync(
        AppDbContext ctx, string? role, string? userId, CancellationToken ct)
    {
        if (!string.Equals(role, "profissional", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(userId))
            return Task.FromResult<string?>(null);

        return ctx.Professionals
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<T> GetOrCreateCachedAsync<T>(
        IMemoryCache cache, string key, TimeSpan ttl, bool bypass,
        Func<Task<T>> factory, ILogger? logger, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!bypass && cache.TryGetValue(key, out T? value) && value is not null)
        {
            logger?.LogDebug("Cache HIT {CacheKey}", key);
            return value;
        }
        logger?.LogDebug("Cache MISS {CacheKey} (bypass={Bypass})", key, bypass);
        var created = await factory();
        cache.Set(key, created, ttl);
        return created;
    }
}
