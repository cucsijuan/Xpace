#!/usr/bin/env python3
"""
Prints the ship's hardware manual to PDF with a Chromium-based browser in
headless mode: Microsoft Edge, Google Chrome or Chromium. hardware.html
becomes Ship-Systems-Manual.pdf.

Usage: python3 build.py [--browser PATH] [hardware]
"""
import argparse, os, shutil, subprocess, sys, tempfile, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
CANDIDATES = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    "google-chrome", "chromium", "chromium-browser", "microsoft-edge",
]


def find_browser():
    for c in CANDIDATES:
        p = c if os.path.isabs(c) else shutil.which(c)
        if p and os.path.exists(p):
            return p
    sys.exit("no Chromium-based browser found: use --browser")


MANUALS = {"hardware": ("hardware.html", "Ship-Systems-Manual.pdf")}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--browser")
    ap.add_argument("manuals", nargs="*", help="hardware (by default, all)")
    args = ap.parse_args()
    for name in args.manuals:
        if name not in MANUALS:
            sys.exit(f"unknown manual {name}: use " + " or ".join(MANUALS))
    browser = args.browser or find_browser()
    for name in args.manuals or list(MANUALS):
        build(browser, *MANUALS[name])


def build(browser, source, pdf):
    out = (HERE / pdf).resolve()
    if out.exists():
        out.unlink()
    profile = tempfile.mkdtemp(prefix="shipman-")
    cmd = [browser, "--headless=new", "--disable-gpu", "--no-first-run", "--user-data-dir=" + profile,
           "--no-pdf-header-footer", "--virtual-time-budget=20000", "--print-to-pdf=" + str(out),
           (HERE / source).as_uri()]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=180)
    for _ in range(60):                     # some builds return before the file is written
        if out.exists() and out.stat().st_size > 0:
            break
        time.sleep(1)
    shutil.rmtree(profile, ignore_errors=True)
    if not out.exists():
        sys.exit("the browser did not write " + str(out))
    print(f"{out.name}: {out.stat().st_size} bytes")


if __name__ == "__main__":
    main()
