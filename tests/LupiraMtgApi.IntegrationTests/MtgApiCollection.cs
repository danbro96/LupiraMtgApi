using Xunit;

namespace LupiraMtgApi.IntegrationTests;

/// <summary>One ephemeral Postgres container shared across the run; integration tests run serially.</summary>
[CollectionDefinition("integration")]
public sealed class MtgApiCollection : ICollectionFixture<MtgApiTestFactory>;
