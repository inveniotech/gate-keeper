// SPDX-License-Identifier: Apache-2.0
import { detectionRule } from '../detection-authoring.mjs';

const processorCategory = 'transfer.processor';
const deploymentDependent = 'Deployment-dependent; confirm the contracted processing and storage regions.';
const entries = [
  { id: 'transfer.processor.paystack', tokens: { hostname: ['api.paystack.co'] },
    categories: ['customer contact details', 'payment and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.flutterwave', tokens: { package: ['flutterwave-node-v3'], hostname: ['api.flutterwave.com'] },
    categories: ['customer contact details', 'payment and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.aws', tokens: { package: ['AWSSDK.S3', '@aws-sdk/client-s3', 'boto3', 'software.amazon.awssdk'],
    hostname: ['s3.amazonaws.com', 's3.eu-west-1.amazonaws.com', 's3.af-south-1.amazonaws.com'] },
    categories: ['application-defined stored data'], region: deploymentDependent },
  { id: 'transfer.processor.aws.bucket', tokens: { package: ['aws_s3_bucket', 'AWS::S3::Bucket'] },
    categories: ['application-defined stored data'], region: deploymentDependent },
  { id: 'transfer.processor.aws.ireland', tokens: { package: ['eu-west-1'] },
    categories: ['application-defined data'], region: 'Europe (Ireland); confirm that this configuration is used.' },
  { id: 'transfer.processor.aws.capetown', tokens: { package: ['af-south-1'] },
    categories: ['application-defined data'], region: 'Africa (Cape Town); confirm that this configuration is used.' },
];
// Public provider documentation reviewed on 6 November 2026. Private endpoints are not inferred.
const additionalEntries = [
  { id: 'transfer.processor.interswitch', tokens: { hostname: ['api.interswitchng.com', 'sandbox.interswitchng.com', 'webpay.interswitchng.com', 'passport-sandbox.interswitchng.com'] },
    categories: ['customer contact details', 'payment and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.palmpay', tokens: { hostname: ['open-gw-prod.palmpay-inc.com', 'open-gw-sandbox.palmpay-inc.com'] },
    categories: ['customer contact details', 'wallet, bank account and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.opay', tokens: { hostname: ['payapi.opayweb.com'] },
    categories: ['customer contact details', 'wallet and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.nibss', tokens: { package: ['ninvoicex-sdk'],
    hostname: ['api-ninvoicex.nibss-plc.com.ng', 'api-ninvoicex-sandbox.nibss-plc.com.ng'] },
    categories: ['invoice party contact and tax details', 'invoice and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.monnify', tokens: { hostname: ['api.monnify.com', 'sandbox.monnify.com'] },
    categories: ['customer contact details', 'bank account and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.remita', tokens: { hostname: ['demo.remita.net', 'login.remita.net', 'remitademo.net', 'api-demo.systemspecsng.com'] },
    categories: ['payer contact details', 'bank account, mandate and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.korapay', tokens: { hostname: ['api.korapay.com'] },
    categories: ['identity and bank account details', 'payment and transaction details'], region: deploymentDependent },
  { id: 'transfer.processor.dojah', tokens: { hostname: ['api.dojah.io'] },
    categories: ['identity and bank account verification details'], region: deploymentDependent },
  { id: 'transfer.processor.youverify', tokens: { package: ['youverify-web-sdk'], hostname: ['api.youverify.co', 'api.sandbox.youverify.co'] },
    categories: ['identity and customer verification details'], region: deploymentDependent },
];
const cases = [];
const addCase = (name, path, text, token, ruleId, expected = true) => {
  cases.push({ name: `p2-processor-${name}`, path, text: text + '\n', expected: expected
    ? [{ ruleId, path, line: 1, column: text.indexOf(token) + 1, triage: 'true-positive' }] : [] });
};
for (const entry of [...entries, ...additionalEntries]) {
  const label = entry.id.split('.').slice(2).join('-');
  for (const [kind, tokens] of Object.entries(entry.tokens)) {
    for (const [index, token] of tokens.entries()) {
      const path = kind === 'hostname' ? 'config/processor.json' : 'src/integration.ts';
      addCase(`${label}-${kind}-${index}`, path, `const processor = "${token}";`, token, entry.id);
      addCase(`${label}-${kind}-${index}-near-miss`, path, `const processor = "prefix-${token}-suffix";`, token, entry.id, false);
    }
  }
}
for (const [name, path, text, token, ruleId] of [
  ['node-manifest', 'package.json', '{"dependencies":{"flutterwave-node-v3":"1.0.0"}}', 'flutterwave-node-v3', 'transfer.processor.flutterwave'],
  ['dotnet-manifest', 'app.csproj', '<Project><ItemGroup><PackageReference Include="AWSSDK.S3" /></ItemGroup></Project>', 'AWSSDK.S3', 'transfer.processor.aws'],
  ['jvm-manifest', 'pom.xml', '<project><dependencies><dependency><groupId>software.amazon.awssdk</groupId></dependency></dependencies></project>', 'software.amazon.awssdk', 'transfer.processor.aws'],
  ['python-manifest', 'requirements.txt', 'boto3', 'boto3', 'transfer.processor.aws'],
  ['infrastructure', 'infra/storage.tf', 'resource "aws_s3_bucket" "synthetic_archive" {}', 'aws_s3_bucket', 'transfer.processor.aws.bucket'],
  ['nibss-public-api', 'config/nibss.json', '{"apiBaseUrl":"https://api-ninvoicex.nibss-plc.com.ng/e-invoice/v1"}', 'api-ninvoicex.nibss-plc.com.ng', 'transfer.processor.nibss'],
  ['nibss-java-manifest', 'pom.xml', '<project><dependencies><dependency><groupId>uk.co.avidroneconsulting</groupId><artifactId>ninvoicex-sdk</artifactId></dependency></dependencies></project>', 'ninvoicex-sdk', 'transfer.processor.nibss'],
]) addCase(name, path, text, token, ruleId);
addCase('lock-file', 'package-lock.json', '{"dependencies":{"flutterwave-node-v3":"1.0.0"}}', '', '', false);
addCase('nibss-portal-only', 'docs/integrations.md', 'Documentation: https://devportal.nibss-plc.com.ng/', '', '', false);

export default {
  family: 'processors', title: 'Processor and transfer catalogue data',
  description: 'Exact-token catalogue rules identify Paystack, Flutterwave, Interswitch, PalmPay, OPay, NIBSS integration references, Monnify, Remita, Korapay, Dojah and Youverify, alongside AWS SDK/S3 infrastructure and two explicit AWS region settings. Every finding is a note asking the organisation to confirm processor safeguards and any transfer basis. No personal-data values are required by these cases.',
  limits: 'Tokens indicate a reference, not actual network activity, processing, recipient role or a transfer. SDKs can be unused; region strings can occur in unrelated text. Matching is exact and case-sensitive, and does not resolve aliases or recognise arbitrary S3 virtual-hosted bucket names. Lock files stay excluded. A provider name or a Nigerian service does not establish the actual processing region: deployment and contractual arrangements require confirmation. Sandbox references also produce notes and do not establish production use. NIBSS coverage uses its publicly documented e-invoicing production/sandbox API hosts and Java SDK artefact. Its developer portal alone does not trigger a finding. Other payment/identity APIs, customer-specific VPN hosts, private addresses and unpublished SDK names are outside this snapshot. OPay coverage uses the Nigerian business documentation at opayweb.com; similarly named unrelated services are excluded. The catalogue is bounded to the sourced entries below, reviewed on 6 November 2026. Processor metadata is retained in loaded rules and this reference; the existing v1 results contract reports the rule identifier and approved citation only. User-facing metadata output remains separate work under #64.',
  fix: 'Review the referenced integration and applicable contracts, record the actual data categories and processing regions, and confirm safeguards and any applicable transfer basis. Use the existing justified-exception configuration when a deliberately retained reference does not need further action.',
  rules: [...entries, ...additionalEntries].map(entry => {
    const { matcher: unused, ...rule } = detectionRule(entry.id, processorCategory, {}, 'note');
    return { ...rule, matcher: { list: entry.tokens }, processor: { dataCategories: entry.categories, usualRegion: entry.region },
      suppressionHints: ['Confirm processor safeguards, the actual data categories and regions, and any applicable transfer basis. A reference alone does not establish a transfer.'] };
  }),
  cases: cases.sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0),
  sources: ['https://paystack.com/docs/api/', 'https://developer.flutterwave.com/docs/authentication',
    'https://docs.interswitchgroup.com/docs/authentication', 'https://docs.interswitchgroup.com/docs/web-checkout',
    'https://docs.palmpay.com/#/en-us/basics-of-access/interface-domain-name',
    'https://docs.palmpay.com/en-us/basics-of-access/interface-domain-name.md',
    'https://documentation.opayweb.com/doc/offline/api-basics.html',
    'https://docs-ninvoicex.nibss-plc.com.ng/guides/test-data',
    'https://docs-ninvoicex.nibss-plc.com.ng/sdks/java', 'https://developers.monnify.com/docs/collections/quickstart',
    'https://api.remita.net/', 'https://github.com/RemitaNet/Remita-Commerce7',
    'https://developers.korapay.com/docs/nigerian-bank-account-verification',
    'https://docs.dojah.io/api-reference/endpoint/general_service/resolve_nuban',
    'https://youverify.co/en/developers', 'https://doc.youverify.co/',
    'https://github.com/Flutterwave/Node-v3', 'https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/s3-apis-intro.html',
    'https://docs.aws.amazon.com/AWSJavaScriptSDK/v3/latest/client/s3/',
    'https://boto3.amazonaws.com/v1/documentation/api/latest/reference/services/s3.html',
    'https://docs.aws.amazon.com/sdk-for-java/latest/developer-guide/setup-project-maven.html',
    'https://docs.aws.amazon.com/general/latest/gr/s3.html',
    'https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/s3_bucket',
    'https://docs.aws.amazon.com/AWSCloudFormation/latest/TemplateReference/aws-resource-s3-bucket.html'],
};
