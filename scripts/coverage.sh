#!/usr/bin/env bash
# Runs the test suite with code coverage, reports the HostedFoundryWorkflow line rate,
# writes coverage-badge.svg, and fails when the line rate is below 100%.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tests="${root}/Hosted-FoundryWorkflow.Tests"
assembly="HostedFoundryWorkflow"
badge="${root}/coverage-badge.svg"

rm -rf "${tests}/TestResults"

(
  cd "${tests}"
  dotnet test -- \
    --coverage \
    --coverage-settings coverage.config \
    --coverage-output-format cobertura \
    --coverage-output coverage.cobertura.xml
)

report="$(find "${tests}/TestResults" -name 'coverage.cobertura.xml' -print -quit)"
if [[ -z "${report}" ]]; then
  echo "coverage: no cobertura report found under ${tests}/TestResults" >&2
  exit 1
fi

percent="$(python3 - "${report}" "${assembly}" <<'PY'
import sys
import xml.etree.ElementTree as ET

report, assembly = sys.argv[1], sys.argv[2]
root = ET.parse(report).getroot()

rates = [float(p.get("line-rate", 0)) for p in root.iter("package") if p.get("name") == assembly]
if not rates:
    sys.exit(f"coverage: assembly '{assembly}' is absent from {report}")

print(f"{min(rates) * 100:.2f}")
PY
)"

echo "coverage: ${assembly} line rate ${percent}%"

label="coverage"
value="$(python3 -c 'import sys; print(f"{float(sys.argv[1]):g}%")' "${percent}")"
if [[ "${percent}" == "100.00" ]]; then
  colour="#4c1"
else
  colour="#fe7d37"
fi

label_width=62
value_width=$(( ${#value} * 7 + 10 ))
total_width=$(( label_width + value_width ))
# Text is placed in a 10x scaled coordinate system, so a centre is (offset + width / 2) * 10.
label_centre=$(( label_width * 5 ))
value_centre=$(( label_width * 10 + value_width * 5 ))

cat > "${badge}" <<SVG
<svg xmlns="http://www.w3.org/2000/svg" width="${total_width}" height="20" role="img" aria-label="${label}: ${value}">
  <title>${label}: ${value}</title>
  <linearGradient id="s" x2="0" y2="100%">
    <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
    <stop offset="1" stop-opacity=".1"/>
  </linearGradient>
  <clipPath id="r"><rect width="${total_width}" height="20" rx="3" fill="#fff"/></clipPath>
  <g clip-path="url(#r)">
    <rect width="${label_width}" height="20" fill="#555"/>
    <rect x="${label_width}" width="${value_width}" height="20" fill="${colour}"/>
    <rect width="${total_width}" height="20" fill="url(#s)"/>
  </g>
  <g fill="#fff" text-anchor="middle" font-family="Verdana,Geneva,DejaVu Sans,sans-serif" font-size="110" text-rendering="geometricPrecision">
    <text x="${label_centre}" y="150" transform="scale(.1)" fill="#010101" fill-opacity=".3">${label}</text>
    <text x="${label_centre}" y="140" transform="scale(.1)">${label}</text>
    <text x="${value_centre}" y="150" transform="scale(.1)" fill="#010101" fill-opacity=".3">${value}</text>
    <text x="${value_centre}" y="140" transform="scale(.1)">${value}</text>
  </g>
</svg>
SVG

echo "coverage: badge written to ${badge}"

if [[ "${percent}" != "100.00" ]]; then
  echo "coverage: below the required 100%" >&2
  exit 1
fi
