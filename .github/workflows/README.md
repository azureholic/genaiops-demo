# GenAIOps workflows

Create the `dev`, `staging`, and `production` GitHub environments before running a
deployment. Configure required reviewers for promotion and rollback (and for candidate
deployment where appropriate). Define these environment variables:

- `AZURE_CLIENT_ID`: client ID of the environment-specific federated identity.
- `AZURE_TENANT_ID`: Microsoft Entra tenant ID.
- `AZURE_SUBSCRIPTION_ID`: target Azure subscription ID.

The identity uses GitHub OIDC; do not add a client secret. Scope its Azure roles to the
target resource group and grant only the data-plane rights needed to push to its
container registry. Configure one federated credential per protected GitHub environment.

Candidate image tags include the release version and commit SHA. The final Container
Apps deployment uses registry digest references from `image-references.json`, so a tag
cannot change the deployed bits. All deployment workflows share an environment-scoped
concurrency group and queue rather than race.

Run the checks that do not require Azure locally:

```powershell
npm --prefix src\Web ci --userconfig src\Web\.npmrc
npm --prefix src\Web run validate:workflows
npm --prefix src\Web run validate:content
az bicep build --file Infrastructure\main.bicep
az bicep lint --file Infrastructure\main.bicep
```
