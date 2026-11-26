<script setup lang="ts">
import { useFormContext } from '@inertiajs/vue3';
import InputError from '@/components/InputError.vue';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

defineProps<{
    name: string;
    label: string;
    type?: string;
    placeholder?: string;
}>();

const form = useFormContext();
</script>

<template>
    <div class="space-y-2">
        <div class="flex items-center justify-between">
            <Label :for="`ctx-${name}`">{{ label }}</Label>
            <span
                v-if="form?.touched(name)"
                class="text-xs text-muted-foreground"
                >touched</span
            >
        </div>
        <Input
            :id="`ctx-${name}`"
            :name="name"
            :type="type ?? 'text'"
            :placeholder="placeholder"
            @blur="form?.validate(name)"
        />
        <InputError :message="form?.errors[name]" />
    </div>
</template>
