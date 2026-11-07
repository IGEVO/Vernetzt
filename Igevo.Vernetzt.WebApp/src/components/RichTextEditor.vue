<script setup lang="ts">
import { onBeforeUnmount, watch } from 'vue'
import { EditorContent, useEditor } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Underline from '@tiptap/extension-underline'

const props = defineProps<{ modelValue: string; disabled?: boolean; headings?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const editor = useEditor({
  content: props.modelValue,
  editable: !props.disabled,
  extensions: [StarterKit.configure({ heading: { levels: [1, 2, 3] } }), Underline, Link.configure({ openOnClick: false, protocols: ['http', 'https', 'mailto'] })],
  onUpdate: ({ editor: instance }) => emit('update:modelValue', instance.getHTML())
})
watch(() => props.modelValue, (value) => { if (editor.value && editor.value.getHTML() !== value) editor.value.commands.setContent(value) })
watch(() => props.disabled, (value) => editor.value?.setEditable(!value))
function setLink(): void {
  const href = window.prompt('Link-Adresse', editor.value?.getAttributes('link').href as string ?? 'https://')
  if (href === null) return
  if (!href) editor.value?.chain().focus().unsetLink().run()
  else editor.value?.chain().focus().extendMarkRange('link').setLink({ href }).run()
}
onBeforeUnmount(() => editor.value?.destroy())
</script>
<template>
  <v-card class="rich-editor" variant="outlined">
    <v-toolbar density="compact" color="surface" aria-label="Textformatierung">
      <template v-if="headings">
        <v-btn icon="mdi-format-header-2" :active="editor?.isActive('heading', { level: 2 })" aria-label="Überschrift" @click="editor?.chain().focus().toggleHeading({ level: 2 }).run()" />
        <v-btn icon="mdi-format-header-3" :active="editor?.isActive('heading', { level: 3 })" aria-label="Zwischenüberschrift" @click="editor?.chain().focus().toggleHeading({ level: 3 }).run()" />
      </template>
      <v-btn icon="mdi-format-bold" :active="editor?.isActive('bold')" aria-label="Fett" @click="editor?.chain().focus().toggleBold().run()" />
      <v-btn icon="mdi-format-italic" :active="editor?.isActive('italic')" aria-label="Kursiv" @click="editor?.chain().focus().toggleItalic().run()" />
      <v-btn icon="mdi-format-underline" :active="editor?.isActive('underline')" aria-label="Unterstrichen" @click="editor?.chain().focus().toggleUnderline().run()" />
      <v-btn icon="mdi-format-list-bulleted" :active="editor?.isActive('bulletList')" aria-label="Aufzählung" @click="editor?.chain().focus().toggleBulletList().run()" />
      <v-btn icon="mdi-format-list-numbered" :active="editor?.isActive('orderedList')" aria-label="Nummerierte Liste" @click="editor?.chain().focus().toggleOrderedList().run()" />
      <v-btn icon="mdi-format-quote-close" :active="editor?.isActive('blockquote')" aria-label="Zitat" @click="editor?.chain().focus().toggleBlockquote().run()" />
      <v-btn icon="mdi-link" :active="editor?.isActive('link')" aria-label="Link" @click="setLink" />
    </v-toolbar>
    <v-divider /><EditorContent :editor="editor" />
  </v-card>
</template>
