// The dashboard is a reader: it fetches the archive the server assembled from the committed
// artifacts and draws it. No figure is computed here beyond formatting.
'use strict';

const state = { archive: null, cases: [], selected: null };

const el = (id) => document.getElementById(id);
const pct = (value) => `${(value * 100).toFixed(1)}%`;
const ms = (value) => `${Math.round(value).toLocaleString()} ms`;
const num = (value, digits = 2) => value.toFixed(digits);

function chip(disposition) {
  const label = disposition ?? 'tied';
  const key = disposition ? disposition.toLowerCase() : 'none';
  const span = document.createElement('span');
  span.className = `chip chip-${key}`;
  span.textContent = label;
  return span;
}

function text(tag, value, className) {
  const node = document.createElement(tag);
  node.textContent = value;
  if (className) node.className = className;
  return node;
}

function definition(list, term, value) {
  list.append(text('dt', term), text('dd', value));
}

async function getJson(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url} returned ${response.status}`);
  return response.json();
}

function renderPolicy(policy) {
  const list = el('policy');
  list.replaceChildren();
  const entries = [
    ['Deny at or below', num(policy.negativeAtOrBelow)],
    ['Requested at or above', num(policy.positiveAtOrAbove)],
    ['Min impact confidence', num(policy.minimumImpactConfidence)],
    ['Max scope expansion', num(policy.maximumAutoApprovedScopeExpansion)],
    ['Read-only impact', policy.readOnlyImpact],
    ['Always approve', policy.approvalRequiredImpacts.join(', ')]
  ];
  for (const [term, value] of entries) {
    const row = document.createElement('div');
    row.append(text('dt', term), text('dd', value));
    list.append(row);
  }
}

function renderLegs(legs) {
  const host = el('legs');
  host.replaceChildren();
  for (const leg of legs) {
    const card = document.createElement('article');
    card.className = 'leg';

    const heading = document.createElement('h3');
    heading.append(text('span', leg.label), text('span', leg.requestedModel, 'model'));
    card.append(heading);

    const list = document.createElement('dl');
    const rows = [
      ['Call-weighted', `${leg.correctRuns}/${leg.successfulRuns} (${pct(leg.callAccuracy)})`],
      ['Per case', `${leg.correctCases}/${leg.labelledCases} (${pct(leg.caseAccuracy)})`],
      ['Unsafe allows', `${leg.unsafeAllowRuns} calls / ${leg.unsafeAllowCases} cases`],
      ['Over-blocks', `${leg.overBlockedRuns} calls / ${leg.overBlockedCases} cases`],
      ['Changed across runs', `${leg.unstableCases} cases`],
      ['Latency p50 / p95', `${ms(leg.latencyP50)} / ${ms(leg.latencyP95)}`],
      ['Tokens in / out', `${leg.inputTokens.toLocaleString()} / ${leg.outputTokens.toLocaleString()}`],
      ['Failed calls', String(leg.failedCalls)]
    ];
    for (const [term, value] of rows) definition(list, term, value);
    card.append(list);
    host.append(card);
  }
}

function renderFamilies(families, legs) {
  const head = el('family-head');
  head.replaceChildren(text('th', 'Family'));
  for (const leg of legs) head.append(text('th', leg.label));

  const body = el('family-body');
  body.replaceChildren();
  for (const family of families) {
    const row = document.createElement('tr');
    row.append(text('td', family.family, 'mono'));
    for (const leg of legs) {
      const entry = family.legs.find((item) => item.legId === leg.id);
      const cell = document.createElement('td');
      if (entry) {
        const wrong = entry.correctCases < entry.labelledCases;
        cell.append(text('span', `${entry.correctCases}/${entry.labelledCases}`, wrong ? 'wrong' : ''));
        cell.append(text('span', ` [${entry.correctRuns}/${entry.labelledRuns}]`, 'muted'));
      } else {
        cell.textContent = '—';
      }
      row.append(cell);
    }
    body.append(row);
  }
}

function renderCaseHead(legs) {
  const head = el('case-head');
  head.replaceChildren(text('th', 'Case'), text('th', 'Family'), text('th', 'Label'));
  for (const leg of legs) head.append(text('th', leg.label));
}

function renderCases(cases) {
  const body = el('case-body');
  body.replaceChildren();
  el('case-count').textContent = `${cases.length} of ${state.total} cases`;

  for (const item of cases) {
    const row = document.createElement('tr');
    row.tabIndex = 0;
    row.dataset.caseId = item.caseId;
    if (state.selected === item.caseId) row.classList.add('selected');

    const id = document.createElement('td');
    id.append(text('div', item.caseId, 'mono'));
    id.append(text('div', item.instruction, 'muted'));
    row.append(id, text('td', item.family, 'mono'));

    const label = document.createElement('td');
    label.append(chip(item.expectedDisposition));
    row.append(label);

    for (const leg of state.archive.legs) {
      const entry = item.legs.find((candidate) => candidate.legId === leg.id);
      const cell = document.createElement('td');
      if (entry) {
        cell.append(chip(entry.majorityDisposition));
        if (!entry.correct) cell.append(text('div', 'wrong', 'flag'));
        if (entry.unsafeAllow) cell.append(text('div', 'unsafe allow', 'flag'));
        if (entry.overBlock) cell.append(text('div', 'over-block', 'flag'));
        if (!entry.stable) cell.append(text('div', 'changed across runs', 'flag'));
        if (entry.calls > 1) {
          cell.append(text('div', `${entry.correctCalls}/${entry.calls} calls`, 'muted'));
        }
      } else {
        cell.textContent = '—';
      }
      row.append(cell);
    }

    row.addEventListener('click', () => select(item.caseId));
    row.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        select(item.caseId);
      }
    });
    body.append(row);
  }
}

function answerLayer(call) {
  const layer = document.createElement('div');
  layer.className = 'layer';
  layer.append(text('h4', '1 · Raw answers'));

  for (const answer of call.answers) {
    const list = document.createElement('dl');
    if (answer.noul !== null && answer.noul !== undefined) {
      definition(list, answer.questionId, num(answer.noul));
    } else if (answer.choice) {
      definition(list, answer.questionId, answer.choice);
      definition(list, 'confidence', num(answer.confidence ?? 0));
    } else if (answer.score !== null && answer.score !== undefined) {
      definition(list, answer.questionId, num(answer.score));
      definition(list, 'confidence', num(answer.confidence ?? 0));
    }
    layer.append(list);

    if (answer.probabilities) {
      const bars = document.createElement('div');
      bars.className = 'bars';
      for (const [name, value] of Object.entries(answer.probabilities)) {
        const bar = document.createElement('div');
        bar.className = 'bar';
        const track = document.createElement('div');
        track.className = 'track';
        const fill = document.createElement('div');
        fill.className = 'fill';
        fill.style.width = `${Math.round(value * 100)}%`;
        track.append(fill);
        bar.append(text('span', name), track, text('span', num(value), 'value'));
        bars.append(bar);
      }
      layer.append(bars);
    }
  }
  return layer;
}

function gateLayer(call) {
  const layer = document.createElement('div');
  layer.className = 'layer';
  layer.append(text('h4', '2 · Gate reading'));

  const list = document.createElement('dl');
  definition(list, 'requested', num(call.gate.requestedProbability));
  definition(list, 'impact', `${call.gate.observedChoice} @ ${num(call.gate.choiceConfidence)}`);
  definition(list, 'scope', num(call.gate.scopeExpansion));
  definition(list, 'risk signals', call.gate.linguisticRiskSignals);
  layer.append(list);

  const reasons = document.createElement('ul');
  for (const reason of call.gate.reasons) reasons.append(text('li', reason));
  layer.append(reasons);
  return layer;
}

function decisionLayer(call) {
  const layer = document.createElement('div');
  layer.className = 'layer';
  layer.append(text('h4', '3 · Decision'));

  const list = document.createElement('dl');
  definition(list, 'disposition', call.disposition);
  definition(list, 'label', call.expectedDisposition);
  definition(list, 'match', call.correct ? 'yes' : 'no');
  definition(list, 'latency', ms(call.durationMilliseconds));
  definition(list, 'tokens in / out', `${call.inputTokens} / ${call.outputTokens}`);
  layer.append(list);

  if (call.expectationFailures.length > 0) {
    layer.append(text('h4', 'Answer expectations missed'));
    const failures = document.createElement('ul');
    for (const failure of call.expectationFailures) failures.append(text('li', failure));
    layer.append(failures);
  }
  return layer;
}

function renderDetail(detail) {
  const host = el('detail');
  host.replaceChildren();
  el('detail-section').hidden = false;

  const head = document.createElement('div');
  head.className = 'detail-head';
  head.append(text('div', detail.instruction, 'instruction'));
  head.append(text('div', `${detail.tool} ${detail.toolArguments}`, 'proposed mono'));

  const meta = document.createElement('div');
  meta.className = 'muted';
  meta.append(document.createTextNode(`${detail.caseId} · ${detail.family} · label `));
  meta.append(chip(detail.expectedDisposition));
  meta.append(document.createTextNode(` · ${detail.repetitions} planned repetitions`));
  head.append(meta);
  head.append(text('div', detail.expectedBehavior, 'muted'));

  if (detail.expectations.length > 0) {
    const expectations = detail.expectations
      .map((expectation) => `${expectation.questionId}: ${expectation.requirement}`)
      .join(' · ');
    head.append(text('div', `Answer expectations — ${expectations}`, 'muted'));
  }
  host.append(head);

  for (const leg of detail.legs) {
    const block = document.createElement('div');
    block.className = 'leg-detail';

    const heading = document.createElement('h3');
    heading.append(text('span', leg.label));
    heading.append(chip(leg.majorityDisposition));
    if (!leg.correct) heading.append(text('span', 'wrong', 'flag'));
    if (!leg.stable) heading.append(text('span', 'changed across runs', 'flag'));
    block.append(heading);

    for (const call of leg.calls) {
      const details = document.createElement('details');
      details.className = 'call';
      if (leg.calls.length === 1 || !call.correct) details.open = true;

      const summary = document.createElement('summary');
      summary.append(text('span', `Run ${call.run}`, 'mono'));
      summary.append(chip(call.disposition));
      summary.append(text('span', call.correct ? 'matches the label' : 'differs from the label',
        call.correct ? 'muted' : 'wrong'));
      summary.append(text('span', ms(call.durationMilliseconds), 'muted'));
      details.append(summary);

      const layers = document.createElement('div');
      layers.className = 'layers';
      layers.append(answerLayer(call), gateLayer(call), decisionLayer(call));
      details.append(layers);
      block.append(details);
    }
    host.append(block);
  }
  el('detail-section').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

async function select(caseId) {
  state.selected = caseId;
  for (const row of document.querySelectorAll('#case-body tr')) {
    row.classList.toggle('selected', row.dataset.caseId === caseId);
  }
  renderDetail(await getJson(`/api/cases/${encodeURIComponent(caseId)}`));
}

async function reload() {
  const params = new URLSearchParams();
  const family = el('family-filter').value;
  const outcome = el('outcome-filter').value;
  const query = el('query-filter').value.trim();
  if (family) params.set('family', family);
  if (outcome && outcome !== 'all') params.set('outcome', outcome);
  if (query) params.set('query', query);

  state.cases = await getJson(`/api/cases?${params}`);
  renderCases(state.cases);
}

async function start() {
  try {
    state.archive = await getJson('/api/archive');
    state.total = (await getJson('/api/cases')).length;

    el('suite-line').textContent =
      `${state.archive.suiteId} · contract ${state.archive.contractId}@${state.archive.contractVersion} · ` +
      `${state.archive.legs.length} recorded runs`;

    renderPolicy(state.archive.policy);
    renderLegs(state.archive.legs);
    renderFamilies(state.archive.families, state.archive.legs);
    renderCaseHead(state.archive.legs);

    const familyFilter = el('family-filter');
    for (const family of state.archive.families) {
      const option = document.createElement('option');
      option.value = family.family;
      option.textContent = family.family;
      familyFilter.append(option);
    }

    const rules = el('rules');
    for (const rule of state.archive.annotationRules) rules.append(text('li', rule));
    el('history').textContent = state.archive.annotationHistory;

    el('filters').addEventListener('submit', (event) => event.preventDefault());
    familyFilter.addEventListener('change', reload);
    el('outcome-filter').addEventListener('change', reload);
    let timer;
    el('query-filter').addEventListener('input', () => {
      clearTimeout(timer);
      timer = setTimeout(reload, 150);
    });

    await reload();
  } catch (error) {
    el('suite-line').textContent = `Could not load the archive: ${error.message}`;
    el('suite-line').classList.add('error');
  }
}

start();
