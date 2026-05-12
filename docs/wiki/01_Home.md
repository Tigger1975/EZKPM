# Welcome to the EZKPM Wiki

**EZKPM (Enterprise Zero-Knowledge Password Manager)** is a highly secure, enterprise-grade password and credential management system. It is designed around the principles of Zero-Knowledge architecture, meaning that the server infrastructure never sees, stores, or processes your plain-text passwords or encryption keys.

## Top-Down Documentation
This wiki provides an exhaustive, top-down guide for both End-Users and Administrators. We start with high-level concepts and drill down into the finest details of usage and configuration.

### Table of Contents

#### 1. High-Level Concepts
* [System Overview & Architecture](02_System_Overview.md)
  * Understand the Zero-Knowledge paradigm, Hybrid-Post-Quantum Cryptography, and the component topology (Server, Client, Extension).

#### 2. End-User Guide
* [User Guide: First Steps & Vault Management](03_User_Guide.md)
  * Initial Device Setup (Zero-Touch SSO)
  * Creating & Managing Assets (Passwords, Passkeys, Payments, SSH/SSL Keys)
  * The Payment Audit Dialog (Revisionssicherheit)
* [Browser Extension & Autofill](05_Browser_Extension.md)
  * Native Messaging Bridge
  * Stealth Injection & Auto-Type

#### 3. Administrator Guide
* [Admin Dashboard & Active Directory](04_Admin_Guide.md)
  * Identity Mapping & Separation of Duties
  * Role-Based Access Control (RBAC: Read, Owner, Execute-Only)
  * Mass-Rollouts & E-Mail Invitations
  * Security Alerts & Vulnerability Scanning
* [Recovery, Logging & Security](06_Recovery_and_Security.md)
  * Emergency Recovery (The 34-character Secret Key & 1Password Principle)
  * Administrator Break-Glass Recovery (Multi-Owner-Principle)
  * Hash-Chaining & Audit Logs extraction

---
*Language Notice: This wiki is maintained in English as the primary language. Translation systems can process these Markdown files into other languages dynamically.*
