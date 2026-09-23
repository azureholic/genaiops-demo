import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('App', () => {
  it('renders the platform scaffold', () => {
    const markup = renderToStaticMarkup(<App />)

    expect(markup).toContain('<h1')
    expect(markup).toContain('GenAIOps Demo Platform')
  })
})
