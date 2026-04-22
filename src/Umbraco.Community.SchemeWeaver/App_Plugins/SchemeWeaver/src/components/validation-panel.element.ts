import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { ValidationIssue, ValidationIssueSeverity } from '../api/types.js';

/**
 * Renders validator findings produced by the Management API `/preview`
 * endpoint. Groups issues by severity (critical → warning → info) and
 * presents each as a row with a severity tag, schema-type chip, JSON path
 * and message — mirroring Google's Rich Results test UI.
 *
 * Styling uses Umbraco UI design tokens (`--uui-color-danger`,
 * `--uui-color-warning`, `--uui-color-positive`) so the panel inherits any
 * active theme.
 *
 * @element schemeweaver-validation-panel
 */
@customElement('schemeweaver-validation-panel')
export class ValidationPanelElement extends UmbLitElement {
  /** Structured validator findings. `undefined` is treated as zero issues. */
  @property({ type: Array, attribute: false })
  issues: ValidationIssue[] | undefined = undefined;

  /**
   * Ordered list of severities. Iterating in this order keeps critical
   * issues at the top and info items at the bottom regardless of server
   * ordering.
   */
  private static readonly SEVERITY_ORDER: ValidationIssueSeverity[] = ['critical', 'warning', 'info'];

  private _groupBySeverity(): Record<ValidationIssueSeverity, ValidationIssue[]> {
    const groups: Record<ValidationIssueSeverity, ValidationIssue[]> = {
      critical: [],
      warning: [],
      info: [],
    };
    for (const issue of this.issues ?? []) {
      // Guard against unexpected severities from a newer backend — default
      // to `info` so the issue is still surfaced rather than silently dropped.
      const bucket = groups[issue.severity] ?? groups.info;
      bucket.push(issue);
    }
    return groups;
  }

  private _iconName(severity: ValidationIssueSeverity): string {
    switch (severity) {
      case 'critical':
        return 'icon-alert';
      case 'warning':
        return 'icon-alert';
      case 'info':
      default:
        return 'icon-info';
    }
  }

  private _tagColor(severity: ValidationIssueSeverity): string {
    switch (severity) {
      case 'critical':
        return 'danger';
      case 'warning':
        return 'warning';
      case 'info':
      default:
        // The backoffice uses neutral-look tags for informational items — no
        // colour attribute falls through to the default grey.
        return '';
    }
  }

  private _severityLabel(severity: ValidationIssueSeverity): string {
    switch (severity) {
      case 'critical':
        return this.localize.term('schemeWeaver_validation_critical') || 'Critical';
      case 'warning':
        return this.localize.term('schemeWeaver_validation_warning') || 'Warning';
      case 'info':
      default:
        return this.localize.term('schemeWeaver_validation_info') || 'Info';
    }
  }

  render() {
    const issues = this.issues ?? [];

    if (issues.length === 0) {
      return html`
        <div class="panel empty" role="status">
          <uui-icon name="icon-check" class="empty-icon"></uui-icon>
          <span class="empty-text">${this.localize.term('schemeWeaver_validation_noIssues')}</span>
        </div>
      `;
    }

    const groups = this._groupBySeverity();

    return html`
      <div class="panel" role="region" aria-label=${this.localize.term('schemeWeaver_validation_heading')}>
        <div class="panel-heading">
          <span class="heading-text">${this.localize.term('schemeWeaver_validation_heading')}</span>
          <span class="summary">
            ${ValidationPanelElement.SEVERITY_ORDER.map((sev) =>
              groups[sev].length > 0
                ? html`<uui-tag
                    look="secondary"
                    color=${this._tagColor(sev)}
                    compact
                    title=${this._severityLabel(sev)}
                  >
                    <uui-icon name=${this._iconName(sev)}></uui-icon>
                    ${groups[sev].length}
                  </uui-tag>`
                : nothing,
            )}
          </span>
        </div>
        <ul class="issue-list">
          ${ValidationPanelElement.SEVERITY_ORDER.flatMap((sev) =>
            groups[sev].map(
              (issue) => html`
                <li class="issue issue-${sev}" data-severity=${sev}>
                  <uui-tag
                    class="severity-tag"
                    look="secondary"
                    color=${this._tagColor(issue.severity)}
                    compact
                  >
                    <uui-icon name=${this._iconName(issue.severity)}></uui-icon>
                    ${this._severityLabel(issue.severity)}
                  </uui-tag>
                  <span class="schema-type-chip" title=${issue.schemaType}>${issue.schemaType}</span>
                  <code class="field-path" title=${issue.path}>${issue.path}</code>
                  <span class="message">${issue.message}</span>
                </li>
              `,
            ),
          )}
        </ul>
      </div>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .panel {
        border-bottom: 1px solid var(--uui-color-border);
      }

      .panel.empty {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        color: var(--uui-color-positive-standalone, var(--uui-color-positive));
        font-size: 0.85rem;
      }

      .panel.empty .empty-icon {
        color: var(--uui-color-positive-standalone, var(--uui-color-positive));
        flex-shrink: 0;
      }

      .panel-heading {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        background-color: var(--uui-color-surface-alt, transparent);
      }

      .heading-text {
        font-weight: 600;
        font-size: 0.9rem;
        color: var(--uui-color-text);
      }

      .summary {
        display: inline-flex;
        gap: var(--uui-size-space-1);
      }

      .issue-list {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      .issue {
        display: grid;
        grid-template-columns: auto auto auto 1fr;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        font-size: 0.85rem;
        border-top: 1px solid var(--uui-color-divider, var(--uui-color-border));
      }

      .severity-tag {
        flex-shrink: 0;
      }

      .schema-type-chip {
        display: inline-block;
        padding: 2px var(--uui-size-space-2);
        background-color: var(--uui-color-surface-alt, var(--uui-color-background));
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius, 3px);
        font-family: 'Courier New', Courier, monospace;
        font-size: 0.75rem;
        color: var(--uui-color-text-alt);
        white-space: nowrap;
      }

      .field-path {
        font-family: 'Courier New', Courier, monospace;
        font-size: 0.8rem;
        color: var(--uui-color-interactive-emphasis, var(--uui-color-interactive));
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        max-width: 28ch;
      }

      .message {
        color: var(--uui-color-text);
        overflow-wrap: anywhere;
      }

      /* Severity-specific accents on the row itself — a thin left border
         echoes the chip colour so users can scan by colour down the list. */
      .issue-critical {
        box-shadow: inset 3px 0 0 0 var(--uui-color-danger);
      }

      .issue-warning {
        box-shadow: inset 3px 0 0 0 var(--uui-color-warning-standalone, var(--uui-color-warning));
      }

      .issue-info {
        box-shadow: inset 3px 0 0 0 var(--uui-color-text-alt);
      }
    `,
  ];
}

export default ValidationPanelElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-validation-panel': ValidationPanelElement;
  }
}
