# -*- coding: utf-8 -*-
"""Проверка: не выходит ли контент за поля страницы A4 landscape."""
import glob, os, sys
import pymupdf

HERE = os.path.dirname(os.path.abspath(__file__))
targets = sorted(glob.glob(os.path.join(HERE, "*.pdf")))
lines = []
for pdf in targets:
    doc = pymupdf.open(pdf)
    lines.append(os.path.basename(pdf))
    for i, page in enumerate(doc):
        h = page.rect.height
        maxy, worst = 0, ""
        for b in page.get_text("blocks"):
            if b[3] > maxy:
                maxy, worst = b[3], b[4].strip().replace("\n", " ")[:50]
        limit = h - 14.2  # поле 5 мм
        lines.append(f"  p{i+1}: низ {maxy:.1f} / предел {limit:.1f} "
                     f"{'OK' if maxy <= limit + 0.5 else 'ВЫХОД'} | {worst}")
open(os.path.join(HERE, "check_report.txt"), "w", encoding="utf-8").write("\n".join(lines))
print("\n".join(lines))
