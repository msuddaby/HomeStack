using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HomeStack.Core;

/// <summary>Request DTOs can implement this for checks DataAnnotations can't express.</summary>
public interface IValidatable
{
    IEnumerable<ValidationResult> Validate();
}

/// <summary>
/// DataAnnotations validation that also honours attributes placed on positional record parameters
/// (<c>record R([Required] string Name)</c>), which the built-in Validator ignores.
/// </summary>
public static class DtoValidator
{
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Prop, ValidationAttribute[] Attrs)[]> Cache = new();

    public static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        foreach (var (prop, attrs) in Cache.GetOrAdd(dto.GetType(), Describe))
        {
            var value = prop.GetValue(dto);
            var ctx = new ValidationContext(dto) { MemberName = prop.Name, DisplayName = prop.Name };
            foreach (var attr in attrs)
                if (attr.GetValidationResult(value, ctx) is { } r && r != ValidationResult.Success)
                    results.Add(new ValidationResult(r.ErrorMessage, [prop.Name]));
        }
        if (dto is IValidatable v) results.AddRange(v.Validate());
        return results;
    }

    private static (PropertyInfo, ValidationAttribute[])[] Describe(Type t)
    {
        var ctorParams = t.GetConstructors().SelectMany(c => c.GetParameters())
            .GroupBy(p => p.Name!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
        return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p =>
            {
                var attrs = p.GetCustomAttributes<ValidationAttribute>(true);
                if (ctorParams.TryGetValue(p.Name, out var param)) attrs = attrs.Concat(param.GetCustomAttributes<ValidationAttribute>(true));
                return (p, attrs.ToArray());
            })
            .Where(x => x.Item2.Length > 0)
            .ToArray();
    }
}

public static class ValidationExtensions
{
    /// <summary>Validates the endpoint argument of type T; returns 400 ValidationProblem on failure.</summary>
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
            if (arg is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["Request body is required."] });

            var results = DtoValidator.Validate(arg);
            if (results.Count == 0) return await next(ctx);
            var errors = results
                .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
                .GroupBy(x => JsonName(x.Member))
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid").ToArray());
            return Results.ValidationProblem(errors);
        });

    private static string JsonName(string member) =>
        string.IsNullOrEmpty(member) ? "body"
        : string.Join('.', member.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
