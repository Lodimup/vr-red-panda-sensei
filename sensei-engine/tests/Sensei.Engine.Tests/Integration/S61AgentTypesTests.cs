using System.Reflection;
using Microsoft.Agents.AI;

namespace Sensei.Engine.Tests.Integration;

/// <summary>S61: the agent is <c>Sensei.Engine.Agent.SenseiAgent</c>, built on <c>ChatClientAgent</c>, with an <c>AIContextProvider</c>.</summary>
public sealed class S61AgentTypesTests
{
    private static readonly Assembly Engine = typeof(SenseiEngine).Assembly;

    [Fact]
    public void S61_agent_and_context_provider_are_present_in_the_engine()
    {
        var agent = Assert.Single(Engine.GetTypes(), t => t.FullName == "Sensei.Engine.Agent.SenseiAgent");
        Assert.Contains(agent.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), f => f.FieldType == typeof(ChatClientAgent) || f.FieldType == typeof(ChatClientAgent).MakeByRefType() || Nullable.GetUnderlyingType(f.FieldType) == typeof(ChatClientAgent));
        Assert.Contains(Engine.GetTypes(), t => typeof(AIContextProvider).IsAssignableFrom(t) && t != typeof(AIContextProvider));
    }
}
