"""Puts stopwatches inside the real physics step, for a measurement: every node's _PhysicsProcess and _Process, each
device of a ship before and after its computer, the computers, power and motion. tests/swarm_test then prints what each
one cost per step. See docs/performance.md.

The changes are temporary and must never be committed. Run it from the repository's root on a clean tree, build, measure,
and take them out with: git checkout scripts tests && rm scripts/Space/StepCosts.cs
"""
import re,glob
n=0
for p in glob.glob('scripts/**/*.cs',recursive=True):
    s=open(p).read(); o=s
    for name,kind in (('_PhysicsProcess','physics'),('_Process','process')):
        def w(m):
            ind,arg=m.group(1),m.group(2)
            t='Timed'+kind
            return (f'{ind}public override void {name}(double {arg})\n{ind}{{\n{ind}    long mark = Xpace.Space.StepCosts.Mark();\n{ind}    {t}({arg});\n'
                    f'{ind}    Xpace.Space.StepCosts.Add(GetType().Name + " {kind}", mark);\n{ind}}}\n\n{ind}private void {t}(double {arg})')
        s=re.sub(r'^( *)public override void '+name+r'\(double (\w+)\)',w,s,flags=re.M)
    if s!=o: open(p,'w').write(s); n+=1
print(n,'files')
p='scripts/Ship/ShipBody.cs'; s=open(p).read()
a='''        WriteControls(bus, Controls);
        WriteInertial(bus);
        WriteRatings(bus);
        foreach (IShipDevice d in _devices)
            d.BeforeStep(this, delta);
'''
b='''        long m0 = Xpace.Space.StepCosts.Mark();
        WriteControls(bus, Controls);
        WriteInertial(bus);
        WriteRatings(bus);
        Xpace.Space.StepCosts.Add(" ship: writing registers", m0);
        foreach (IShipDevice d in _devices)
        {
            long m1 = Xpace.Space.StepCosts.Mark();
            d.BeforeStep(this, delta);
            Xpace.Space.StepCosts.Add(" before: " + d.GetType().Name, m1);
        }
'''
assert a in s; s=s.replace(a,b)
a='''        foreach (IShipDevice d in _devices)
            d.AfterStep(this, delta);
'''
b='''        foreach (IShipDevice d in _devices)
        {
            long m1 = Xpace.Space.StepCosts.Mark();
            d.AfterStep(this, delta);
            Xpace.Space.StepCosts.Add(" after: " + d.GetType().Name, m1);
        }
'''
assert a in s; s=s.replace(a,b)
a='''        if (Computing.Count > 1)
            Parallel.For(0, Computing.Count, RunComputer);
        else if (Computing.Count == 1)
            RunComputer(0);
'''
b='''        long m2 = Xpace.Space.StepCosts.Mark();
        if (Computing.Count > 1)
            Parallel.For(0, Computing.Count, RunComputer);
        else if (Computing.Count == 1)
            RunComputer(0);
        Xpace.Space.StepCosts.Add(" all computers", m2);
'''
assert a in s; s=s.replace(a,b)
a2='''        Powered(delta);
        Push((float)delta);
    }

    /// <summary>The ship as the integrator'''
b2='''        long m3 = Xpace.Space.StepCosts.Mark();
        Powered(delta);
        Xpace.Space.StepCosts.Add(" ship: power", m3);
        m3 = Xpace.Space.StepCosts.Mark();
        Push((float)delta);
        Xpace.Space.StepCosts.Add(" ship: push (motion, contacts)", m3);
    }

    /// <summary>The ship as the integrator'''
assert a2 in s; s=s.replace(a2,b2); open(p,'w').write(s)
open('scripts/Space/StepCosts.cs','w').write('''using System.Collections.Generic;
using System.Diagnostics;
namespace Xpace.Space;
public static class StepCosts
{
    public static bool On;
    public static readonly Dictionary<string, long> Ticks = new();
    public static long Mark() => On ? Stopwatch.GetTimestamp() : 0;
    public static void Add(string what, long mark)
    {
        if (On)
            Ticks[what] = Ticks.GetValueOrDefault(what) + Stopwatch.GetTimestamp() - mark;
    }
}
''')
p='tests/SwarmTest.cs'; s=open(p).read()
a='''        var steps = new List<double>();
'''
b='''        StepCosts.On = true;
        var steps = new List<double>();
'''
assert a in s; s=s.replace(a,b)
a='''        steps.Sort();
        double mean'''
b='''        StepCosts.On = false;
        double total = steps.Sum();
        foreach (var (what, ticks) in StepCosts.Ticks.OrderByDescending(k => k.Value).Take(34))
            GD.Print($"     COST {ticks * 1000.0 / Stopwatch.Frequency / steps.Count,7:0.000} ms {ticks * 1000.0 / Stopwatch.Frequency / total * 100,5:0.0}% {what}");
        steps.Sort();
        double mean'''
assert a in s; s=s.replace(a,b); open(p,'w').write(s)
