---
trigger: always_on
description: Global rule set for development, language, specifications, and project-specific guidelines.
---

# Global Rule Set

The following rules apply globally to all development tasks, projects, and workspaces. They must be strictly followed by the AI Agent.

## 1. Language & Internationalization (i18n)
* **Default Language**: All system outputs, logs, code, comments, documentation, and the Graphical User Interface (GUI) must be written and defined in **English** by default.
* **Translation Support**: The GUI must be designed and implemented to fully support localization (translation).
* **Target Languages**: 
  * The primary translation language that must be supported and implemented is **German (Deutsch)**.
  * Translation resources (e.g., `.resx` files, JSON localization dictionaries, or custom localization services) must be kept up-to-date.

## 2. Requirements & Specifications (Pflichtenheft)
* **Adherence**: All existing requirements documents, project specifications (e.g., `Rules.aimd` or separate "Pflichtenheft" documents) must be strictly adhered to.
* **Conflict Resolution**: In case of conflicting or contradictory instructions, the instructions in the **newest version** of the requirements document or specification supersede older ones.

## 3. Project-Specific Rules & Prompts
* **Precedence**: Local project-specific rules, guidelines, and prompts (e.g., `SystemPrompt.aimd` or files in `.agents/rules/`) must always be respected and applied.

## 4. Agent Guidelines (`.agents` / `.agent` subfolders)
* **Observation**: All configuration, instruction, and rule files located in `.agents` or `.agent` directories at the project root must be parsed, respected, and followed.

## 5. Capturing Learned Project Information
* **Documentation of Knowledge**: Newly acquired or learned project-specific information, architectural decisions, API quirks, or workflow exceptions must be documented as instructions.
* **Storage Location**: These learned instructions must be written directly into the respective project's `.agents` folder (e.g., under `.agents/rules/` or `.agents/learned_rules.md`) to ensure persistence and future adherence.
