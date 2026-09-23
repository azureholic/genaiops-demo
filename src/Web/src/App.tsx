import { Card, CardHeader, Text, Title1 } from '@fluentui/react-components'
import './App.css'

function App() {
  return (
    <main>
      <header>
        <Text weight="semibold">Azure AI Foundry</Text>
        <Title1 as="h1">GenAIOps Demo Platform</Title1>
        <Text>
          Prompt versioning, safe evaluation, promotion, and rollback.
        </Text>
      </header>

      <Card className="status-card">
        <CardHeader header={<Text weight="semibold">Platform scaffold</Text>} />
        <Text>
          The application shell is ready. Dashboard workflows are added by later
          implementation tasks.
        </Text>
      </Card>
    </main>
  )
}

export default App
