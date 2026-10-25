#!/usr/bin/env python3
"""Writes scripts/Net/EosProduct.g.cs (not committed) with the game's Epic Online Services product keys, from a JSON
file ({"productId", "sandboxId", "deploymentId", "clientId", "clientSecret"}, from the developer portal) given as the
argument, or from the environment (EOS_PRODUCT_ID, EOS_SANDBOX_ID, EOS_DEPLOYMENT_ID, EOS_CLIENT_ID,
EOS_CLIENT_SECRET). Without the file, the game reads the same variables when it starts."""
import json, os, sys

names = ["productId", "sandboxId", "deploymentId", "clientId", "clientSecret"]
env = ["EOS_PRODUCT_ID", "EOS_SANDBOX_ID", "EOS_DEPLOYMENT_ID", "EOS_CLIENT_ID", "EOS_CLIENT_SECRET"]
keys = json.load(open(sys.argv[1])) if len(sys.argv) > 1 else {n: os.environ.get(e, "") for n, e in zip(names, env)}
missing = [n for n in names if not keys.get(n)]
if missing:
    sys.exit("missing EOS keys: " + ", ".join(missing))
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "..", "scripts", "Net", "EosProduct.g.cs")
args = ", ".join(json.dumps(keys[n]) for n in names)
with open(out, "w", newline="\n") as f:
    f.write("// Written by tools/eos_keys.py; not committed.\n#if XPACE_EOS\nnamespace Xpace.Net;\n\n"
            "public sealed partial class EosNet\n{\n"
            "    static partial void Load(ref GodotEos.EosKeys keys) => keys = new(" + args + ");\n}\n#endif\n")
print("wrote", os.path.normpath(out))
