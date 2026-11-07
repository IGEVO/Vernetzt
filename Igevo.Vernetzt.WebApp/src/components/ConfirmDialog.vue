<script setup lang="ts">
withDefaults(defineProps<{ modelValue: boolean; title: string; text: string; confirmText?: string; confirmColor?: string }>(), { confirmText: 'Löschen', confirmColor: 'error' })
defineEmits<{ 'update:modelValue': [value: boolean]; confirm: [] }>()
</script>
<template>
  <v-dialog :model-value="modelValue" max-width="500" @update:model-value="$emit('update:modelValue', $event)">
    <v-card :title="title" :text="text">
      <v-card-actions><v-spacer /><v-btn @click="$emit('update:modelValue', false)">Abbrechen</v-btn><v-btn :color="confirmColor" variant="flat" @click="$emit('confirm')">{{ confirmText }}</v-btn></v-card-actions>
    </v-card>
  </v-dialog>
</template>
