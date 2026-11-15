# -*- coding: utf-8 -*-
import glob, os, hashlib
import pymupdf

HERE = os.path.dirname(os.path.abspath(__file__))
report = []
first_pages = {}
for pdf in sorted(glob.glob(os.path.join(HERE, "*.pdf"))):
    doc = pymupdf.open(pdf)
    name = os.path.basename(pdf)
    sizes = {f"{p.rect.width:.1f}x{p.rect.height:.1f}" for p in doc}
    fonts = set()
    for p in doc:
        for f in p.get_fonts():
            fonts.add(f[3])
    t1 = doc[0].get_text()
    key = hashlib.md5(t1.encode()).hexdigest()
    first_pages[name] = key
    imgs = sum(len(p.get_images()) for p in doc)
    report.append(f"{name}: страниц {len(doc)}, размер {', '.join(sizes)}, шрифты {', '.join(sorted(fonts))}, "
                  f"изображений {imgs}, текст 1-й стр. {len(t1)} симв.")
report.append("")
report.append("Первые страницы идентичны: " + str(len(set(first_pages.values())) == 1))
open(os.path.join(HERE, "verify_report.txt"), "w", encoding="utf-8").write("\n".join(report))
print("\n".join(report))
