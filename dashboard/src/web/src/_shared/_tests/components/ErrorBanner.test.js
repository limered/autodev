import { describe, it, expect } from 'vitest'
import { createSSRApp, h } from 'vue'
import { renderToString } from 'vue/server-renderer'
import ErrorBanner from '../../../_shared/components/ErrorBanner.vue'

// Rendered via SSR so the markup contract can be asserted without a DOM or
// extra test-utils dependency.
async function renderBanner(props) {
  const app = createSSRApp({ render: () => h(ErrorBanner, props) })
  return renderToString(app)
}

describe('ErrorBanner', () => {
  it('renders an alert region with the title and message', async () => {
    const html = await renderBanner({ title: 'Sync failed', message: 'HTTP 500' })
    expect(html).toContain('class="error-banner"')
    expect(html).toContain('role="alert"')
    expect(html).toMatch(/<strong[^>]*>Sync failed<\/strong>/)
    expect(html).toMatch(/<p[^>]*>HTTP 500<\/p>/)
  })
})
