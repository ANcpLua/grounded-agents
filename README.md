# Multi-Agenten-Lösung mit Microsoft Foundry — Referenzlösung zur Abschlussaktivität

![Coverage](coverage-badge.svg)

Dieses Repository ist eine vollständige, in C# umgesetzte Antwort auf die Abschlussaktivität
in [GOAL.md](GOAL.md): eine produktionsreife Multi-Agenten-Lösung entwerfen, absichern und
bereitstellen. Es ist als Lernbeispiel für Kursteilnehmer gedacht — jeder Schritt der Aufgabe
hat hier ein lauffähiges Gegenstück.

## Aufbau

| Kapitel | Inhalt | GOAL.md-Bezug |
|---|---|---|
| [Hosted-FoundryIQ](Hosted-FoundryIQ/) | Foundry-managed Agent mit Foundry-IQ-Wissensbasis (Azure AI Search), aufgerufen aus einer C#-Konsolen-App | Schritt 1: Agent mit Wissensquelle |
| [Hosted-FoundryMcpTools](Hosted-FoundryMcpTools/) | Agent mit remote MCP-Tool (Microsoft Learn) und eigenem lokalen stdio-MCP-Server (Inventar-Tools) | Schritt 1: Agent mit Tools |
| [Hosted-FoundryWorkflow](Hosted-FoundryWorkflow/) | Beide Agents orchestriert zu einem Workflow (Produktberatung → Bestandsprüfung → Konsolidierung), mit OpenTelemetry-Tracing und Evaluation-Gate | Schritte 1–3: Multi-Agent, Observability, Evaluierung, End-to-End-Workflow |
| [Hosted-FoundryWorkflow.Tests](Hosted-FoundryWorkflow.Tests/) | Testsuite für die Domänentypen des Workflows, mit Coverage-Messung | Schritt 2: Zuverlässigkeit und Wartbarkeit |

## Das Geschäftsszenario

Ein Einzelhandels-Copilot: Eine Kundenanfrage („Was hilft bei trockener Haut, und ist es
vorrätig?") durchläuft drei spezialisierte Agents.

1. **Produktexperte** — Foundry-managed, beantwortet Produktfragen ausschließlich aus der
   Foundry-IQ-Wissensbasis.
2. **Bestandsanalyst** — prüft Lagerstand und Abverkauf über lokale MCP-Tools und leitet
   Restock-/Clearance-Empfehlungen ab.
3. **Konsolidierer** — fasst beide geprüften Ergebnisse zu einer Empfehlung für Kunde und
   Filiale zusammen, ohne eigene Tools.

Ein Multi-Agenten-Ansatz trennt die Verantwortlichkeiten: Wissensabruf, operative Daten und
Synthese haben unterschiedliche Werkzeuge, Fehlerbilder und Prüfkriterien. Die Übergaben
zwischen den Agents sind typisiert; eine Stage erhält nur belegte Ergebnisse der vorherigen.

## Produktionsreife

- **Observability** (GOAL.md Schritt 2.1): Jede Workflow-Stage erzeugt OpenTelemetry-Spans
  mit Tool-Call-Evidenz, Approval-Entscheidungen und Verdict; Export nach Application
  Insights oder zur Konsole. Siehe [Hosted-FoundryWorkflow](Hosted-FoundryWorkflow/README.md).
- **Evaluierung** (Schritt 2.2): `dotnet run -- eval` führt ein Evaluation-Gate über die
  Stage-Agents aus — deterministische Checks, Tool-Call-Erwartungen und optional
  Foundry-server-seitige Evaluatoren. Der Prozess endet nur mit Exit 0, wenn jede Stufe
  lief und bestand.
- **Governance und Zuverlässigkeit** (Schritt 2.3): Tool-Approvals sind deny-by-default über
  eine geschlossene Policy; Antworten gelten erst als verwertbar, wenn ihre Tool-Aufrufe
  belegt sind. `dotnet run -- selftest` prüft diese Zusicherungen offline; die Testsuite
  deckt die Domänentypen ab.

## Einstieg

Voraussetzungen und Portal-Einrichtung stehen in den Kapitel-READMEs; die Reihenfolge
Hosted-FoundryIQ → Hosted-FoundryMcpTools → Hosted-FoundryWorkflow baut aufeinander auf.

```bash
cd Hosted-FoundryWorkflow
dotnet run -- selftest      # offline, ohne Azure-Zugang
dotnet run -- "Frage..."    # kompletter Workflow (Foundry-Projekt erforderlich)
dotnet run -- eval          # Evaluation-Gate
```
