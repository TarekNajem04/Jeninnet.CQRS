//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Configurations;

/// <summary>
/// Root configuration for the CQRS library.
/// Hosts can bind these settings from <c>appsettings.json</c>.
/// </summary>
public record CqrsOptions(PipelineBehaviorsOptions PipelineBehaviors = null!) {
    /// <summary>
    /// The default configuration section name for binding.
    /// </summary>
    public static readonly string SectionName = "Cqrs";

    /// <summary>
    /// Gets the configuration options for the pipeline behaviors.
    /// </summary>
    public PipelineBehaviorsOptions PipelineBehaviors { get; set; } = PipelineBehaviors;

    /// <summary>
    /// Gets or sets the notification publishing strategy.
    /// </summary>
    public NotificationPublishStrategy NotificationStrategy { get; set; } = NotificationPublishStrategy.ForEachAwait;
}