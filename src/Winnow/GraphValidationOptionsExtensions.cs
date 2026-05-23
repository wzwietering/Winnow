using Winnow.Internal.Validation;

namespace Winnow;

/// <summary>
/// Fluent helpers for attaching a pre-validation pipeline to a graph options
/// object: <see cref="InsertGraphOptions"/>, <see cref="GraphOptions"/>,
/// <see cref="DeleteGraphOptions"/>, or <see cref="UpsertGraphOptions"/>.
/// </summary>
/// <remarks>
/// Overloads are provided per concrete options type so the fluent chain keeps
/// the caller's exact derived type. The resulting
/// <see cref="GraphValidationOptions"/> exposes
/// <see cref="GraphValidationOptions.IncludeNavigations"/> for walking children;
/// the <c>WithDataAnnotations</c> overloads expose <c>includeNavigations</c> as
/// a parameter so the safe combination is reachable in one call.
/// </remarks>
public static class GraphValidationOptionsExtensions
{
    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, WinnowValidator{TEntity}, ValidationFailureBehavior)"/>
    public static InsertGraphOptions WithValidation<TEntity>(
        this InsertGraphOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, WinnowValidator{TEntity}, ValidationFailureBehavior)"/>
    public static GraphOptions WithValidation<TEntity>(
        this GraphOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, WinnowValidator{TEntity}, ValidationFailureBehavior)"/>
    public static DeleteGraphOptions WithValidation<TEntity>(
        this DeleteGraphOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <summary>
    /// Attaches a delegate-driven pre-validation pipeline to a graph options
    /// object. The resulting <see cref="GraphValidationOptions"/> exposes
    /// <see cref="GraphValidationOptions.IncludeNavigations"/> for walking
    /// child entities — note that <c>IncludeNavigations</c> requires a
    /// DataAnnotations validator (use <c>WithDataAnnotations</c> instead) and
    /// will reject assignment otherwise.
    /// </summary>
    /// <param name="options">The options receiver.</param>
    /// <param name="validator">The validator delegate.</param>
    /// <param name="onFailure">
    /// What to do when at least one entity fails validation. Default:
    /// <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </param>
    public static UpsertGraphOptions WithValidation<TEntity>(
        this UpsertGraphOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions, bool, ValidationFailureBehavior)"/>
    public static InsertGraphOptions WithDataAnnotations<TEntity>(
        this InsertGraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions, bool, ValidationFailureBehavior)"/>
    public static GraphOptions WithDataAnnotations<TEntity>(
        this GraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions, bool, ValidationFailureBehavior)"/>
    public static DeleteGraphOptions WithDataAnnotations<TEntity>(
        this DeleteGraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    /// <summary>
    /// Attaches a DataAnnotations-driven pre-validation pipeline to a graph
    /// options object. Pass <paramref name="includeNavigations"/> to descend
    /// into navigation properties and validate reachable children in the same
    /// call.
    /// </summary>
    /// <param name="options">The options receiver.</param>
    /// <param name="includeNavigations">
    /// When <c>true</c>, the walker validates entities reachable through
    /// navigation properties up to the configured depth limit. Default: <c>false</c>.
    /// </param>
    /// <param name="onFailure">
    /// What to do when at least one entity fails validation. Default:
    /// <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </param>
    public static UpsertGraphOptions WithDataAnnotations<TEntity>(
        this UpsertGraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    private static void ConfigureGraph<TEntity>(
        GraphOptionsBase options, WinnowValidator<TEntity> validator, ValidationFailureBehavior onFailure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        var validation = GraphValidationOptions.Create(validator);
        validation.FailureBehavior = onFailure;
        options.Validation = validation;
    }

    private static void ConfigureGraphDataAnnotations<TEntity>(
        GraphOptionsBase options, bool includeNavigations, ValidationFailureBehavior onFailure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var validator = DataAnnotationsValidatorFactory.Create<TEntity>();
        var validation = GraphValidationOptions.CreateInternal(typeof(TEntity), validator, isDataAnnotationsValidator: true);
        validation.FailureBehavior = onFailure;
        if (includeNavigations) validation.IncludeNavigations = true;
        options.Validation = validation;
    }
}
