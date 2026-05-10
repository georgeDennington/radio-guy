namespace RadioMan.Agents;

public sealed class AgentRouter : IDisposable
{
    private readonly IReadOnlyList<RadioAgent> _agents;

    public AgentRouter(IEnumerable<RadioAgent> agents)
    {
        _agents = agents.ToArray();
    }

    public (RadioAgent Agent, RadioCall Call)? Route(string transcript)
    {
        foreach (var agent in _agents)
        {
            var call = agent.Parser.Parse(transcript);
            if (call is { AddressedToRecipient: true })
                return (agent, call);
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var a in _agents) a.Dispose();
    }
}
