"""Read the reference XLSX without changing it; emit the embedded tablet catalogue.

Uses only Python's standard library. Rankings and all source columns remain unchanged.
"""
import argparse
import hashlib
import json
import re
import sys
import unicodedata
import zipfile
from collections import Counter
from pathlib import Path
from xml.etree import ElementTree as ET

NS = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
TYPES = dict(zip(
    ["Irradiée", "Superviseur", "Brèche", "Expédition", "Délire", "Rituel", "Abysse", "Temple"],
    ["Irradiated Tablet", "Overseer Tablet", "Breach Tablet", "Expedition Tablet", "Delirium Tablet", "Ritual Tablet", "Abyss Tablet", "Temple Tablet"]))


def sheets(path):
    with zipfile.ZipFile(path) as archive:
        strings = []
        if "xl/sharedStrings.xml" in archive.namelist():
            strings = ["".join(e.itertext()) for e in ET.fromstring(archive.read("xl/sharedStrings.xml")).findall("s:si", NS)]
        relations = {e.attrib["Id"]: e.attrib["Target"].lstrip("/") for e in ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))}
        result = {}
        for sheet in ET.fromstring(archive.read("xl/workbook.xml")).findall("s:sheets/s:sheet", NS):
            relation = sheet.attrib["{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"]
            target = relations[relation]
            if not target.startswith("xl/"):
                target = "xl/" + target
            rows = []
            for row in ET.fromstring(archive.read(target)).findall("s:sheetData/s:row", NS):
                values = []
                for cell in row.findall("s:c", NS):
                    if cell.find("s:f", NS) is not None:
                        raise ValueError("Unexpected formula in source: " + sheet.attrib["name"] + "!" + cell.attrib["r"])
                    column = 0
                    for char in re.match(r"[A-Z]+", cell.attrib["r"])[0]:
                        column = column * 26 + ord(char) - 64
                    while len(values) < column:
                        values.append(None)
                    value = cell.find("s:v", NS)
                    kind = cell.attrib.get("t", "n")
                    if kind == "inlineStr":
                        values[column - 1] = "".join(cell.find("s:is", NS).itertext())
                    elif value is not None:
                        text = value.text or ""
                        values[column - 1] = strings[int(text)] if kind == "s" else (float(text) if kind == "n" else text)
                rows.append(values)
            result[sheet.attrib["name"]] = rows
        return result


def text_key(text):
    # Ignore only typography and numeric rolls. Retain words, signs and singular/plural.
    text = unicodedata.normalize("NFKC", text).casefold().replace("’", "'").replace("–", "-").replace("−", "-")
    text = re.sub(r"\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)|\d+(?:\.\d+)?", "#", text)
    text = text.replace("an additional", "# additional")
    return re.sub(r"\s+", " ", text).strip()


def import_catalog(source, legacy_path):
    workbook = sheets(source)
    table = workbook["TOUS LES MODS"]
    header = table[0]
    legacy = json.loads(legacy_path.read_text(encoding="utf-8-sig"))["mods"]
    entries, unmapped = [], []
    seen, subranks = set(), set()
    for row_number, row in enumerate(table[1:], 2):
        fields = dict(zip(header, row))
        canonical = fields["Tablette"]
        game_type = TYPES[canonical]
        code, rank = fields["Code"], fields["Rang"]
        if not re.fullmatch(r"[SABCDEF][1-9]\d*", rank):
            raise ValueError("Invalid individual rank: " + str(fields))
        if (canonical, code) in seen or (canonical, rank) in subranks:
            raise ValueError("Duplicate code or subrank: " + str(fields))
        seen.add((canonical, code))
        subranks.add((canonical, rank))
        english = fields["Texte du jeu EN"]
        first_clause = english.split(";")[0].strip()
        matches = [m for m in legacy if game_type in m["TabletTypes"] and text_key(m["Name"]) == text_key(first_clause)]
        # The existing catalogue has a grammatical typo: "an extra packs".
        if canonical == "Temple" and code == "T03" and not matches:
            matches = [m for m in legacy if m["Id"] == "TowerIncursionExtraPacksChance" and
                       text_key(m["Name"].replace("extra packs of", "extra pack of")) == text_key(first_clause)]
        if len(matches) > 1:
            raise ValueError("Ambiguous game identifier: " + str(fields))
        game_ids = [m["Id"] for m in matches]
        if not game_ids:
            unmapped.append({"Type": canonical, "Code": code, "English": english})
        entries.append({
            "Id": canonical + ":" + code, "TabletType": game_type, "CanonicalType": canonical,
            "Code": code, "Rank": rank[0], "SubRank": int(rank[1:]), "RankLabel": rank,
            "EnglishText": english, "FrenchText": fields["Mod FR"],
            "Affix": {"Préfixe": "prefix", "Suffixe": "suffix"}[fields["Affixe"]],
            "Justification": fields["Pourquoi ce rang"], "RankOrigin": fields["Origine du rang"],
            "Source": fields["Source joueurs / guides"], "AffixSource": fields["Base affixes"],
            "Strategy": fields["Stratégie principale"], "Family": fields["Famille"],
            "GameModIds": game_ids, "Availability": "active", "SourceRow": row_number, "SourceFields": fields,
            "MatchEvidence": [m["Id"] + " — " + m["Name"] for m in matches],
        })
    for canonical in TYPES:
        master = [r[1:] for r in table[1:] if r[0] == canonical]
        if master != workbook[canonical][1:]:
            raise ValueError("Per-tablet sheet differs from master: " + canonical)
        quick = next(r for r in workbook["LECTURE RAPIDE"][1:] if r[0] == canonical)
        counts = Counter(r[1][0] for r in master)
        if len(master) != quick[1] or [counts[c] for c in "SABCDEF"] != quick[2:9]:
            raise ValueError("Summary counts differ: " + canonical)
    return {
        "Version": "0.5.5", "SourceFile": source.name,
        "SourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "RankOrder": list("SABCDEF"), "Types": TYPES,
        "MethodAndSources": [dict(zip(workbook["MÉTHODE ET SOURCES"][0], r)) for r in workbook["MÉTHODE ET SOURCES"][1:]],
        "Modifiers": entries, "ImportIssues": unmapped,
    }


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--output", type=Path, default=Path("Plugins/StashUtility/Data/tablet_meta_rankings.json"))
    parser.add_argument("--legacy", type=Path, default=Path("Plugins/StashUtility/Data/tablet_mod_ranges.json"))
    args = parser.parse_args()
    catalog = import_catalog(args.source, args.legacy)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Imported", len(catalog["Modifiers"]), "modifiers across", len(TYPES), "tablet types.")
    print("Unmapped game identifiers:", len(catalog["ImportIssues"]))
    for entry in catalog["ImportIssues"]:
        print(entry["Type"], entry["Code"], entry["English"])
