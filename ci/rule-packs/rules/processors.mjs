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
const cases = [];
const addCase = (name, path, text, token, ruleId, expected = true) => {
  cases.push({ name: `p2-processor-${name}`, path, text: text + '\n', expected: expected
    ? [{ ruleId, path, line: 1, column: text.indexOf(token) + 1, triage: 'true-positive' }] : [] });
};
for (const entry of entries) {
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
]) addCase(name, path, text, token, ruleId);
addCase('lock-file', 'package-lock.json', '{"dependencies":{"flutterwave-node-v3":"1.0.0"}}', '', '', false);

export default {
  family: 'processors', title: 'Processor and transfer catalogue data',
  description: 'Exact-token catalogue rules identify payment integrations, AWS SDK use, S3 infrastructure resource types and two explicit AWS region settings. Every finding is a note asking the organisation to confirm processor safeguards and any transfer basis. No personal-data values are required by these cases.',
  limits: 'Tokens indicate a reference, not actual network activity, processing, recipient role or a transfer. SDKs can be unused; region strings can occur in unrelated text. Matching is exact and case-sensitive, and does not resolve aliases or recognise arbitrary S3 virtual-hosted bucket names. Lock files stay excluded. SDK use alone cannot establish a processing region, so its metadata is deployment-dependent. The first catalogue is deliberately bounded to the sourced entries below. Processor metadata is retained in loaded rules and this reference; the existing v1 results contract reports the rule identifier and approved citation only. User-facing metadata output remains separate work under #64.',
  fix: 'Review the referenced integration and applicable contracts, record the actual data categories and processing regions, and confirm safeguards and any applicable transfer basis. Use the existing justified-exception configuration when a deliberately retained reference does not need further action.',
  rules: entries.map(entry => {
    const { matcher: unused, ...rule } = detectionRule(entry.id, processorCategory, {}, 'note');
    return { ...rule, matcher: { list: entry.tokens }, processor: { dataCategories: entry.categories, usualRegion: entry.region },
      suppressionHints: ['Confirm processor safeguards, the actual data categories and regions, and any applicable transfer basis. A reference alone does not establish a transfer.'] };
  }),
  cases: cases.sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0),
  sources: ['https://paystack.com/docs/api/', 'https://developer.flutterwave.com/docs/authentication',
    'https://github.com/Flutterwave/Node-v3', 'https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/s3-apis-intro.html',
    'https://docs.aws.amazon.com/AWSJavaScriptSDK/v3/latest/client/s3/',
    'https://boto3.amazonaws.com/v1/documentation/api/latest/reference/services/s3.html',
    'https://docs.aws.amazon.com/sdk-for-java/latest/developer-guide/setup-project-maven.html',
    'https://docs.aws.amazon.com/general/latest/gr/s3.html',
    'https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/s3_bucket',
    'https://docs.aws.amazon.com/AWSCloudFormation/latest/TemplateReference/aws-resource-s3-bucket.html'],
};
