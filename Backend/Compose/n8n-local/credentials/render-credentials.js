#!/usr/bin/env node
const fs = require('fs');

const [, , inputPath, outputPath] = process.argv;
if (!inputPath || !outputPath) {
  console.error('Usage: render-credentials.js <input> <output>');
  process.exit(1);
}

const template = fs.readFileSync(inputPath, 'utf8');
const rendered = template.replace(/\$\{([A-Z_][A-Z0-9_]*)\}/g, (_, name) => {
  const value = process.env[name];
  if (value === undefined || value === '') {
    throw new Error(`[render-credentials] required env var ${name} is not set`);
  }
  return JSON.stringify(value).slice(1, -1);
});

fs.writeFileSync(outputPath, rendered);
console.log(`[render-credentials] wrote ${outputPath}`);
