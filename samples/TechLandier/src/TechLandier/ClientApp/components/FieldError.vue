<script setup lang="ts">
defineProps<{ message?: string | null }>()
</script>

<!-- Server validation message under a field; slides in so a new error is noticed without shifting focus. -->
<template>
  <Transition
    enter-active-class="transition duration-200 ease-(--ease-out-soft)" enter-from-class="-translate-y-1 opacity-0"
    leave-active-class="transition duration-150" leave-to-class="opacity-0"
  >
    <p v-if="message" role="alert" class="text-destructive text-xs leading-5">{{ message }}</p>
  </Transition>
</template>
