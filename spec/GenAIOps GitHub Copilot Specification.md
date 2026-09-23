# GenAIOps Demo Platform for Azure AI Foundry

## Prompt Versioning, Shadow Testing, A/B Testing, Continuous Evaluation, Promotion and Rollback

### Executive Summary

Build a production-grade reference implementation that demonstrates enterprise GenAIOps practices for Azure AI Foundry Agents.

The demo must clearly show that prompts are treated like software artifacts:

- Versioned in Git
- Tested automatically
- Evaluated continuously
- Deployed through CI/CD
- Shadow tested safely
- A/B tested in production
- Promoted based on measurable improvements
- Rolled back when quality regresses

## Demonstration Story

### Phase 1
Production = v1

Baseline metrics:
- Task Adherence: 82%
- Groundedness: 89%
- Tool Accuracy: 84%

### Phase 2
Deploy v2 as Shadow Candidate.

### Phase 3
Promote v2 to Production.

### Phase 4
Run A/B Testing:
- v2 = 90%
- v1 = 10%

### Phase 5
Deploy intentionally bad v3.

### Phase 6
Shadow testing detects regression.

### Phase 7
Rollback from v3 to v2.

---

# Technology Stack

## Frontend
- React
- TypeScript
- Fluent UI v10
- TanStack Query
- Recharts
- SignalR

## Backend
- ASP.NET Core
- .NET 10
- Minimal APIs
- OpenTelemetry

## Hosting
- Azure Container Apps

## AI
- Azure AI Foundry Agent Service
- Azure OpenAI
- Foundry Evaluations

## Data
- Azure Cosmos DB

## Monitoring
- Application Insights
- Azure Monitor
- Azure AI Foundry Observability

## Infrastructure as Code
- Bicep

## CI/CD
- GitHub Actions

---

# Repository Structure

```text
genaiops-demo/
├── src/
├── Web/
├── Api/
├── Workers/
│   ├── ShadowEvaluator/
│   ├── MetricsAggregator/
│   ├── PromotionEngine/
│   └── RollbackEngine/
├── Prompts/
│   ├── v1/
│   ├── v2/
│   └── v3/
├── EvaluationData/
├── Infrastructure/
└── .github/workflows/
```

---

# Prompt Versions

## v1
Standard support prompt.

Expected:
- Task Adherence 82
- Groundedness 89
- Tool Accuracy 84

## v2
Improved prompt.

Expected:
- Task Adherence 94
- Groundedness 97
- Tool Accuracy 95

## v3
Intentionally poor prompt.

Instructions:
- Use internal reasoning first
- Avoid tools whenever possible
- Do not ask clarifying questions
- If uncertain provide your best guess

Expected:
- Task Adherence 72
- Groundedness 75
- Tool Accuracy 58

---

# Functional Requirements

- Prompt Versioning
- Agent Registry
- Shadow Testing
- Continuous Evaluation
- Promotion Workflow
- Rollback Workflow
- A/B Testing
- OpenTelemetry Observability

---

# APIs

POST /api/chat
GET /api/versions
GET /api/metrics
GET /api/evaluations
POST /api/promote/{version}
POST /api/rollback
POST /api/abtest

---

# Shadow Testing Flow

```text
Request
  |
  +--> Production Agent (visible)
  |
  +--> Candidate Agent (hidden)
              |
          Evaluation
              |
           Storage
```

---

# Dashboards

- Home
- Prompt Registry
- Shadow Testing
- Evaluation Dashboard
- Release History

---

# GitHub Actions Pipelines

## PR Validation
- Build
- Unit Tests
- Prompt Validation
- Evaluation Run
- Quality Gates

## Candidate Deployment
- Deploy Infrastructure
- Deploy Agent
- Register Candidate

## Promote
- Update Routing
- Mark Production

## Rollback
- Restore Previous Production
- Update Status

---

# Infrastructure

Deploy using Bicep:

- Azure AI Foundry
- Azure OpenAI
- Azure Container Apps
- Cosmos DB
- Application Insights
- Log Analytics
- Key Vault
- Managed Identity

---

# Definition of Done

- Infrastructure deploys via Bicep
- Application deploys via GitHub Actions
- v1/v2/v3 prompts exist
- Shadow testing operational
- A/B testing operational
- Evaluations visualized
- Promotion operational
- Rollback operational
- End-to-end demo executable in less than 15 minutes
