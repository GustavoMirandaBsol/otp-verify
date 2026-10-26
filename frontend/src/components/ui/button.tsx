import * as React from 'react';import {Slot} from '@radix-ui/react-slot';import {cva,type VariantProps} from 'class-variance-authority';import {cn} from '../../lib/utils';
const variants=cva('btn',{variants:{variant:{default:'btn-primary',outline:'btn-secondary',ghost:'hover:bg-slate-100'}},defaultVariants:{variant:'default'}});
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement>,VariantProps<typeof variants>{asChild?:boolean}
export const Button=React.forwardRef<HTMLButtonElement,ButtonProps>(({className,variant,asChild=false,...props},ref)=>{const Comp=asChild?Slot:'button';return <Comp className={cn(variants({variant}),className)} ref={ref} {...props}/>;});Button.displayName='Button';
