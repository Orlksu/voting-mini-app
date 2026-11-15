# -*- coding: utf-8 -*-
import glob, os, subprocess, sys, urllib.parse
import pymupdf

HERE = os.path.dirname(os.path.abspath(__file__))
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

html_files = sorted(glob.glob(os.path.join(HERE, "*.html")))
if len(sys.argv) > 1:
    html_files = [f for f in html_files if sys.argv[1] in os.path.basename(f)]

for h in html_files:
    pdf = os.path.splitext(h)[0] + ".pdf"
    url = "file:///" + urllib.parse.quote(h.replace("\\", "/"))
    cmd = [EDGE, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
           "--run-all-compositor-stages-before-draw", "--virtual-time-budget=6000",
           f"--print-to-pdf={pdf}", url]
    r = subprocess.run(cmd, capture_output=True, text=True)
    ok = os.path.exists(pdf)
    size = os.path.getsize(pdf) if ok else 0
    print(("OK  " if ok else "FAIL"), os.path.basename(pdf), size)
    if not ok:
        print(r.stdout, r.stderr)
        continue
    doc = pymupdf.open(pdf)
    for i, page in enumerate(doc):
        pix = page.get_pixmap(dpi=110)
        out = os.path.splitext(pdf)[0] + f"_p{i+1}.png"
        pix.save(out)
    print("    pages:", len(doc))
