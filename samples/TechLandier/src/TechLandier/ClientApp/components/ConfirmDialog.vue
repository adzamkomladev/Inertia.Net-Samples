<script setup lang="ts">
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'

defineProps<{
  open: boolean
  title: string
  description?: string
  confirmLabel?: string
  destructive?: boolean
  processing?: boolean
}>()

const emit = defineEmits<{ 'update:open': [value: boolean]; confirm: [] }>()
</script>

<!-- Confirmation dialog for irreversible admin/customer actions. -->
<template>
  <AlertDialog :open="open" @update:open="(v: boolean) => emit('update:open', v)">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ title }}</AlertDialogTitle>
        <AlertDialogDescription>{{ description }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel :disabled="processing">Cancel</AlertDialogCancel>
        <AlertDialogAction
          :class="cn(destructive && buttonVariants({ variant: 'destructive' }))"
          :disabled="processing"
          @click="emit('confirm')"
        >
          {{ confirmLabel ?? 'Confirm' }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
