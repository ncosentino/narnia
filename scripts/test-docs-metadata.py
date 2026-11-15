"""Exercise descriptions through the real documentation templates and hooks."""

from html.parser import HTMLParser
import json
from pathlib import Path
import re
import tempfile
import unittest

from mkdocs.commands.build import build
from mkdocs.config import load_config

ROOT = Path(__file__).resolve().parents[1]


class Metadata(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.in_head = False
        self.values = {}

    def handle_starttag(self, tag, attrs):
        if tag == 'head':
            self.in_head = True
        elif tag == 'meta' and self.in_head:
            attrs = dict(attrs)
            name = attrs.get('property') or attrs.get('name')
            if name:
                self.values.setdefault(name, []).append(attrs.get('content'))

    def handle_endtag(self, tag):
        if tag == 'head':
            self.in_head = False


class DocumentationMetadataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workspace = tempfile.TemporaryDirectory(dir=ROOT, prefix='.metadata-fixtures-')
        cls.addClassCleanup(cls.workspace.cleanup)
        root = Path(cls.workspace.name)
        docs = root / 'docs'
        cls.site = root / 'site'
        pages = {
            'index.md': '# Home\n',
            'guide.md': (
                '# Guide\n\n> Ignore this note.\n\n'
                '![Diagram](https://example.test/diagram.png)\n\n'
                'Use `Session<T>` with **history** &amp; "checkpoints".\n\nIgnore later prose.\n'
            ),
            'authored.md': (
                '---\ndescription: \'Explicit "quoted" <T> & safe summary.\'\n---\n'
                '# Authored\n\nIgnore this introduction.\n'
            ),
            'blank.md': "---\ndescription: '   '\n---\n# Blank\n\nBrowse sessions.\n",
            'empty.md': '# Empty\n\n```text\nnot introductory prose\n```\n',
            'long.md': '# Long\n\n' + 'Browse sessions and inspect recorded conversation history. ' * 15,
            'getting-started.md': (
                '# Getting Started\n\nUse `</script>` &amp; "checkpoints".\n\n'
                '## Configure\n\nBrowse sessions.\n'
            ),
            'tools/fixture.md': (
                '# Fixture\n\nInspect recorded sessions.\n\n'
                '## Parameters\n\nUse `</script>` &amp; "checkpoints".\n'
            ),
            'notes.md': (
                '# Notes\n\n!!! note\n    Ignore this advisory.\n\n'
                '| Ignore | This |\n| --- | --- |\n| table | contents |\n\n'
                '- Ignore this list.\n\nRead recorded history.\n'
            ),
        }
        for source, content in pages.items():
            path = docs / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding='utf-8')
        config = load_config(
            config_file=str(ROOT / 'mkdocs.yml'), docs_dir=str(docs),
            site_dir=str(cls.site), site_url='https://example.test/docs/',
            nav=list(pages), extra_javascript=[],
        )
        cls.site_name = config.site_name
        cls.home_description = config.site_description
        build(config)

    def read(self, source):
        path = self.site / ('index.html' if source == 'index' else f'{source}/index.html')
        html = path.read_text(encoding='utf-8')
        metadata = Metadata()
        metadata.feed(html)
        return html, metadata.values

    def description(self, source):
        _, values = self.read(source)
        result = values.get('description', [])
        self.assertEqual(len(result), 1, source)
        for tag in ('og:description', 'twitter:description'):
            self.assertEqual(values.get(tag), result, (source, tag))
        return result[0]

    def test_intro_preserves_inline_text_and_skips_notes_and_images(self):
        self.assertEqual(self.description('guide'),
                         'Guide. Use Session<T> with history & "checkpoints".')

    def test_authored_override_is_preserved_and_safely_rendered(self):
        self.assertEqual(self.description('authored'), 'Explicit "quoted" <T> & safe summary.')

    def test_homepage_preserves_project_description(self):
        self.assertEqual(self.description('index'), self.home_description)

    def test_blank_override_derives_intro(self):
        self.assertEqual(self.description('blank'), 'Blank. Browse sessions.')

    def test_missing_prose_has_truthful_identity_fallback(self):
        self.assertEqual(self.description('empty'), f'{self.site_name}: Empty.')

    def test_admonitions_tables_and_lists_are_not_introductions(self):
        self.assertEqual(self.description('notes'), 'Notes. Read recorded history.')

    def test_long_intro_ends_at_word_boundary(self):
        description = self.description('long')
        self.assertLessEqual(len(description), 240)
        self.assertTrue(description.endswith('...'))
        self.assertRegex(description[:-3], r'(Browse|sessions|and|inspect|recorded|conversation|history\.)$')

    def test_existing_headings_and_content_are_preserved(self):
        html, _ = self.read('guide')
        self.assertRegex(html, r'<h1[^>]*>Guide(?:<a[^>]*>.*?</a>)?</h1>')
        self.assertIn('Ignore later prose.', html)

    def schemas(self, source):
        html, _ = self.read(source)
        scripts = re.findall(r'<script type="application/ld\+json">\s*(.*?)\s*</script>',
                             html, re.DOTALL)
        return [json.loads(script) for script in scripts]

    def test_source_description_stays_plain_and_safe_in_howto_json_ld(self):
        howto = next(schema for schema in self.schemas('getting-started')
                     if schema.get('@type') == 'HowTo')
        self.assertEqual(howto['description'], self.description('getting-started'))
        self.assertIn('</script>', howto['description'])

    def test_faq_json_ld_preserves_plain_text_without_closing_script(self):
        faq = next(schema for schema in self.schemas('tools/fixture')
                   if schema.get('@type') == 'FAQPage')
        self.assertIn('</script>', faq['mainEntity'][0]['acceptedAnswer']['text'])

    def test_llms_descriptions_remain_decoded_plain_text(self):
        content = (self.site / 'llms.txt').read_text(encoding='utf-8')
        self.assertIn('Guide. Use Session<T> with history & "checkpoints".', content)
        self.assertNotIn('&lt;T&gt;', content)


if __name__ == '__main__':
    unittest.main()
