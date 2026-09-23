import {
  Button,
  FluentProvider,
  Spinner,
  Text,
  webDarkTheme,
  webLightTheme,
} from '@fluentui/react-components'
import {
  BeakerRegular,
  ChartMultipleRegular,
  ClockRegular,
  HomeRegular,
  NavigationRegular,
  bundleIcon,
  HomeFilled,
  BeakerFilled,
  ChartMultipleFilled,
  ClockFilled,
} from '@fluentui/react-icons'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Component, lazy, Suspense, useEffect, useMemo, useState, type ErrorInfo, type ReactNode } from 'react'
import {
  BrowserRouter,
  NavLink,
  Route,
  Routes,
} from 'react-router-dom'
import { ApiError } from './api/client'
import { RealtimeProvider, useRealtime, type RealtimeConnectionFactory } from './realtime/RealtimeProvider'
import './App.css'

const HomePage = lazy(() => import('./pages/HomePage'))
const PromptRegistryPage = lazy(() => import('./pages/PromptRegistryPage'))
const ReleaseHistoryPage = lazy(() => import('./pages/ReleaseHistoryPage'))
const ShadowTestingPage = lazy(() => import('./pages/ShadowTestingPage'))
const EvaluationDashboardPage = lazy(() => import('./pages/EvaluationDashboardPage'))

const HomeIcon = bundleIcon(HomeFilled, HomeRegular)
const RegistryIcon = bundleIcon(BeakerFilled, BeakerRegular)
const EvaluationIcon = bundleIcon(ChartMultipleFilled, ChartMultipleRegular)
const HistoryIcon = bundleIcon(ClockFilled, ClockRegular)

const navItems = [
  { to: '/', label: 'Home', icon: HomeIcon },
  { to: '/prompts', label: 'Prompt Registry', icon: RegistryIcon },
  { to: '/shadow-testing', label: 'Shadow Testing', icon: BeakerRegular },
  { to: '/evaluations', label: 'Evaluation Dashboard', icon: EvaluationIcon },
  { to: '/releases', label: 'Release History', icon: HistoryIcon },
] as const

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (attempt, error) =>
        !(error instanceof ApiError && error.status >= 400 && error.status < 500)
        && attempt < 2,
      staleTime: 30_000,
      refetchOnWindowFocus: true,
    },
  },
})

interface ErrorBoundaryState {
  error?: Error
}

class AppErrorBoundary extends Component<{ children: ReactNode }, ErrorBoundaryState> {
  state: ErrorBoundaryState = {}

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('The dashboard could not render.', error, info)
  }

  render() {
    if (this.state.error) {
      return (
        <main className="fatal-error" role="alert">
          <h1>Something went wrong</h1>
          <Text>Reload the dashboard to try again. No changes were made.</Text>
          <Button appearance="primary" onClick={() => window.location.reload()}>
            Reload dashboard
          </Button>
        </main>
      )
    }

    return this.props.children
  }
}

function AppShell() {
  const [navOpen, setNavOpen] = useState(false)
  const { status } = useRealtime()

  return (
    <div className="app-shell">
      <header className="top-bar">
        <Button
          className="nav-toggle"
          appearance="subtle"
          aria-label={navOpen ? 'Close navigation' : 'Open navigation'}
          aria-expanded={navOpen}
          aria-controls="primary-navigation"
          icon={<NavigationRegular />}
          onClick={() => setNavOpen((open) => !open)}
        />
        <div>
          <Text weight="semibold">GenAIOps</Text>
          <Text size={200} className="product-subtitle">Azure AI Foundry operations</Text>
        </div>
        <span className={`connection connection--${status}`} role="status" aria-live="polite">
          <span aria-hidden="true" className="connection__dot" />
          {status === 'connected' ? 'Live' : status === 'connecting' ? 'Connecting' : 'Offline'}
        </span>
      </header>

      <nav
        id="primary-navigation"
        className={`side-nav ${navOpen ? 'side-nav--open' : ''}`}
        aria-label="Primary navigation"
      >
        {navItems.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            end={to === '/'}
            className={({ isActive }) => `nav-link ${isActive ? 'nav-link--active' : ''}`}
            onClick={() => setNavOpen(false)}
          >
            <Icon aria-hidden="true" />
            <span>{label}</span>
          </NavLink>
        ))}
      </nav>

      <main id="main-content" className="page-content" tabIndex={-1}>
        <Suspense fallback={<div className="page-loading"><Spinner label="Loading page" /></div>}>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/prompts" element={<PromptRegistryPage />} />
            <Route path="/shadow-testing" element={<ShadowTestingPage />} />
            <Route path="/evaluations" element={<EvaluationDashboardPage />} />
            <Route path="/releases" element={<ReleaseHistoryPage />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
        </Suspense>
      </main>
      {navOpen && <button className="nav-scrim" aria-label="Close navigation" onClick={() => setNavOpen(false)} />}
    </div>
  )
}

function NotFound() {
  return (
    <section className="placeholder-page">
      <Text as="h1" size={800} weight="semibold">Page not found</Text>
      <Text>The requested dashboard page does not exist.</Text>
      <Button appearance="primary" as="a" href="/">Return home</Button>
    </section>
  )
}

function ThemeProvider({ children }: { children: ReactNode }) {
  const [dark, setDark] = useState(() => document.documentElement.dataset.theme === 'dark')

  useEffect(() => {
    const observer = new MutationObserver(() => {
      setDark(document.documentElement.dataset.theme === 'dark')
    })
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    return () => observer.disconnect()
  }, [])

  const theme = useMemo(() => {
    const base = dark ? webDarkTheme : webLightTheme
    return {
      ...base,
      colorNeutralBackground1: 'var(--cp-bg)',
      colorNeutralBackground2: 'var(--cp-bg-elevated)',
      colorNeutralForeground1: 'var(--cp-text)',
      colorNeutralForeground2: 'var(--cp-text-muted)',
      colorNeutralStroke1: 'var(--cp-border)',
      colorBrandBackground: 'var(--cp-accent)',
      colorBrandForeground1: 'var(--cp-accent)',
      colorNeutralForegroundOnBrand: 'var(--cp-accent-fg)',
      fontFamilyBase: '"Segoe UI", Aptos, Calibri, -apple-system, BlinkMacSystemFont, sans-serif',
    }
  }, [dark])

  return <FluentProvider theme={theme}>{children}</FluentProvider>
}

export interface AppProps {
  realtimeFactory?: RealtimeConnectionFactory
  client?: QueryClient
}

export default function App({ realtimeFactory, client = queryClient }: AppProps) {
  return (
    <AppErrorBoundary>
      <ThemeProvider>
        <QueryClientProvider client={client}>
          <RealtimeProvider factory={realtimeFactory}>
            <BrowserRouter>
              <a className="skip-link" href="#main-content">Skip to main content</a>
              <AppShell />
            </BrowserRouter>
          </RealtimeProvider>
        </QueryClientProvider>
      </ThemeProvider>
    </AppErrorBoundary>
  )
}
