using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Data;

public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options)
    : DbContext(options);